using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace AGCleaner.Core
{
    /// <summary>一条残留项：可能是目录、注册表项、快捷方式或失效启动项。</summary>
    public class OrphanItem
    {
        public string GroupId = "";
        public string Kind = "";      // dir / reg / lnk / startup
        public string Name = "";
        public string Target = "";    // 目录路径 / 注册表项路径 / 快捷方式文件
        public string Detail = "";
        public long Bytes = 0;
        public string Hive = "HKLM";
        public string KeyPath = "";
        public bool AdminNeeded = false;
        /// <summary>弱证据项：同名软件仍在别处安装，这里可能只是旧版本副本，默认不勾选。</summary>
        public bool Weak = false;
        public object Data;
    }

    public class OrphanGroup
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public bool AdminOnly = false;
        public List<OrphanItem> Items = new List<OrphanItem>();
    }

    /// <summary>残留清理：查找已卸载软件留下的目录、无效卸载注册表项、失效快捷方式与启动项。</summary>
    public static class Orphans
    {
        private class Installed
        {
            public string Name = "";
            public string Location = "";
            public string Icon = "";
            public string Uninstall = "";
        }

        private static readonly string[] SkipDirs = new string[]
        {
            "microsoft", "windows", "common files", "windows nt", "windows defender", "windowsapps",
            "modifiable", "temp", "packages", "package cache", "installer", "agcleaner", "public",
            "system", "config", "drivers", "desktop.ini", "dotnet", "reference assemblies",
            "windows defender advanced threat protection", "comms", "connecteddevicesplatform",
            "ebwebview", "webview", "edgeupdate", "edgecore", "microsoft edge", "python"
        };

        private const int MinAgeDays = 90;
        private const long MinBytes = 20 * 1024 * 1024;

        /// <summary>目录扫描的整体时间预算：宁可少报，也不让“扫描”变成无底洞。</summary>
        private static DateTime _dirDeadline = DateTime.MaxValue;
        private static readonly int DirBudgetSeconds = 40;

        private static bool OutOfBudget()
        {
            return DateTime.Now > _dirDeadline;
        }

        /// <summary>目录里有卸载程序（uninst*.exe / unins*.exe），说明它原本是一个软件安装目录。</summary>
        private static bool HasUninstaller(string dir)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly); }
            catch (Exception) { return false; }
            for (int i = 0; i < files.Length; i++)
            {
                string n = Path.GetFileName(files[i]).ToLowerInvariant();
                if (!n.EndsWith(".exe")) continue;
                if (n.StartsWith("uninst") || n.StartsWith("unins") || n.StartsWith("unwise") || n == "uninstall.exe") return true;
            }
            return false;
        }


        /// <summary>目录里最近一次文件写入时间（只看前两层，够判断有没有人在用）。</summary>
        private static DateTime NewestWrite(string dir)
        {
            DateTime newest = DateTime.MinValue;
            try { newest = Directory.GetLastWriteTime(dir); }
            catch (Exception) { }
            WalkNewest(dir, 0, 2, ref newest, 0);
            return newest;
        }

        private static void WalkNewest(string dir, int depth, int maxDepth, ref DateTime newest, int visited)
        {
            if (depth > maxDepth || visited > 3000) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i];
                try
                {
                    FileAttributes attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    DateTime t = File.GetLastWriteTime(e);
                    if (t > newest) newest = t;
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        WalkNewest(e, depth + 1, maxDepth, ref newest, visited + 1);
                    }
                }
                catch (Exception) { }
            }
        }

        public static List<OrphanGroup> Scan(Action<string> report)
        {
            return Scan(report, null);
        }

        public static List<OrphanGroup> Scan(Action<string> report, StepProgress prog)
        {
            List<OrphanGroup> groups = new List<OrphanGroup>();
            if (prog != null) prog(4, Lang.T("读取已安装程序索引..."));
            List<Installed> apps = ReadInstalled();
            R(report, Lang.T("读取已安装程序索引：") + apps.Count + Lang.T(" 条"));
            if (prog != null) prog(12, Lang.T("扫描孤儿安装目录..."));
            OrphanGroup dirs = ScanDirs(apps, report, prog);
            groups.Add(dirs);
            if (prog != null) prog(45, Lang.T("扫描残留注册表键..."));
            groups.Add(ScanRegResidue(dirs, report));
            if (prog != null) prog(58, Lang.T("扫描无效卸载注册表项..."));
            groups.Add(ScanRegistry(report));
            if (prog != null) prog(72, Lang.T("扫描失效快捷方式..."));
            groups.Add(ScanShortcuts(report));
            if (prog != null) prog(88, Lang.T("扫描失效启动项..."));
            groups.Add(ScanStartup(report));
            if (prog != null) prog(92, Lang.T("扫描空目录与空文件..."));
            groups.Add(ScanEmptyDirs(report));
            groups.Add(ScanEmptyFiles(report));
            if (prog != null) prog(97, Lang.T("扫描空注册表键..."));
            groups.Add(ScanEmptyRegKeys(report));
            if (prog != null) prog(100, Lang.T("残留扫描完成"));
            return groups;
        }

        private static void R(Action<string> report, string msg) { if (report != null) report(msg); }

        private static string Str(object o)
        {
            if (o == null) return "";
            return o.ToString();
        }

        // ---------- 已安装程序索引 ----------

        private static readonly string[] UninstallRoots = new string[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private static List<Installed> ReadInstalled()
        {
            List<Installed> list = new List<Installed>();
            ReadHive(Registry.LocalMachine, UninstallRoots[0], list);
            ReadHive(Registry.LocalMachine, UninstallRoots[1], list);
            ReadHive(Registry.CurrentUser, UninstallRoots[0], list);
            return list;
        }

        private static void ReadHive(RegistryKey hive, string rootPath, List<Installed> list)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(rootPath))
                {
                    if (root == null) return;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(names[i]))
                            {
                                if (k == null) continue;
                                string display = Str(k.GetValue("DisplayName"));
                                if (display.Length == 0) continue;
                                Installed a = new Installed();
                                a.Name = display;
                                a.Location = Str(k.GetValue("InstallLocation")).Trim('"');
                                a.Icon = Str(k.GetValue("DisplayIcon")).Trim('"');
                                a.Uninstall = Str(k.GetValue("UninstallString")).Trim('"');
                                list.Add(a);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        // ---------- 孤儿安装目录 ----------

        private static OrphanGroup ScanDirs(List<Installed> apps, Action<string> report, StepProgress prog)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "dir";
            g.Name = Lang.T("孤儿安装目录");
            g.Desc = Lang.T("已卸载软件留下、且没有被任何已安装程序引用的目录（默认不勾选，逐个确认）");
            g.AdminOnly = true;
            R(report, Lang.T("扫描孤儿安装目录..."));
            _dirDeadline = DateTime.Now.AddSeconds(DirBudgetSeconds);

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ra = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string lap = Path.Combine(la, "Programs");

            // 系统盘上的常规位置
            int step = 0;
            bool sysOn = DriveAllowed(pf);
            step = Step(prog, step, 14, Lang.T("扫描 Program Files（系统盘）..."));
            if (sysOn) ScanRoot(g, apps, pf, 90, false);
            step = Step(prog, step, 18, Lang.T("扫描 Program Files (x86)（系统盘）..."));
            if (sysOn) ScanRoot(g, apps, pf86, 90, false);
            step = Step(prog, step, 21, Lang.T("扫描用户 Programs 目录..."));
            if (sysOn) ScanRoot(g, apps, lap, 60, false);
            step = Step(prog, step, 24, Lang.T("扫描 ProgramData..."));
            if (sysOn) ScanRoot(g, apps, pd, 60, false);
            step = Step(prog, step, 27, Lang.T("扫描 LocalAppData..."));
            if (sysOn) ScanRoot(g, apps, la, 40, false);
            step = Step(prog, step, 30, Lang.T("扫描 Roaming..."));
            if (sysOn) ScanRoot(g, apps, ra, 40, false);

            // 其他磁盘上的同类位置（软件常装到 D/E 盘，这里以前漏扫）
            string[] driveRoots = DriveRoots();
            for (int i = 0; i < driveRoots.Length; i++)
            {
                string dr = driveRoots[i];
                step = Step(prog, step, 32 + i * 3, Lang.T("扫描 ") + dr + Lang.T(" 下的软件目录..."));
                if (!DriveAllowed(dr)) continue;
                ScanRoot(g, apps, Path.Combine(dr, "Program Files"), 60, false);
                ScanRoot(g, apps, Path.Combine(dr, "Program Files (x86)"), 60, false);
                ScanRoot(g, apps, Path.Combine(dr, "Programs"), 40, false);
                // 盘根目录下的厂商目录（例如 D:\360、D:\2345Soft），只认带卸载程序的那种
                ScanRoot(g, apps, dr, 30, true);
            }
            return g;
        }

        /// <summary>设置里限定扫描磁盘时，只扫勾选的盘；为空表示全部固定盘。</summary>
        public static string DriveOverride = "C"; // 默认只扫 C 盘，用户要清别的盘再改

        private static bool DriveAllowed(string root)
        {
            try
            {
                string sel = Settings.Load().ScanDrives; if (sel == null || sel.Trim().Length == 0) sel = DriveOverride;
                if (sel == null || sel.Trim().Length == 0) return true;
                string letter = Path.GetPathRoot(root).TrimEnd('\\').ToUpperInvariant();
                string[] parts = sel.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Trim().ToUpperInvariant() == letter) return true;
                }
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static int Step(StepProgress prog, int step, int percent, string msg)
        {
            if (percent <= step) percent = step + 1;
            if (prog != null) prog(percent, msg);
            return percent;
        }

        /// <summary>所有固定磁盘的根路径（不含光驱、可移动盘）。</summary>
        private static string[] DriveRoots()
        {
            List<string> list = new List<string>();
            System.IO.DriveInfo[] all;
            try { all = System.IO.DriveInfo.GetDrives(); }
            catch (Exception) { return new string[0]; }
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (all[i].DriveType != System.IO.DriveType.Fixed) continue;
                    if (!all[i].IsReady) continue;
                    list.Add(all[i].RootDirectory.FullName);
                }
                catch (Exception) { }
            }
            return list.ToArray();
        }

        private static readonly string[] CommonUserDirs = new string[]
        {
            "users", "windows", "program files", "program files (x86)", "programdata", "programs",
            "projects", "project", "downloads", "documents", "desktop", "pictures", "videos", "music",
            "backup", "backups", "data", "temp", "tmp", "cache", "games", "steam", "steamapps",
            "tools", "work", "workspace", "code", "src", "iso", "vm", "vms", "movie", "media",
            "onedrive", "bin", "drivers", "logs", "dump", "dumps", "reference", "release"
        };

        private static bool IsCommonUserDir(string name)
        {
            string low = name.ToLowerInvariant();
            for (int i = 0; i < CommonUserDirs.Length; i++) if (low == CommonUserDirs[i]) return true;
            return false;
        }

        private static void ScanRoot(OrphanGroup g, List<Installed> apps, string root, int max, bool driveRoot)
        {
            if (root.Length == 0 || !Directory.Exists(root)) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch (Exception) { return; }
            int added = 0;
            string owner;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (OutOfBudget()) return;
                if (g.Items.Count > 90) return;
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (Skip(name)) continue;
                if (Guard.IsWhitelisted(dir)) continue;
                // 盘根目录只处理像软件安装目录的（带卸载程序、非用户常见文件夹）
                if (driveRoot && IsCommonUserDir(name)) continue;

                int strength = RefStrength(dir, apps, out owner);
                if (strength == 0)
                {
                    if (!HasUninstaller(dir))
                    {
                        if (IsCopyDir(dir))
                        {
                            if (AddCopy(g, root, dir, name)) added++;
                            continue;
                        }
                        // 厂商父目录本身不带卸载程序（例如 D:\360），继续看它的下一层
                        added += ScanChildren(g, apps, dir, max - added);
                        continue;
                    }
                    if (Evaluate(g, root, dir, name, added)) added++;
                    continue;
                }
                if (strength == 1 && HasUninstaller(dir))
                {
                    if (AddWeak(g, root, dir, name, owner)) added++;
                    continue;
                }

                // 厂商父目录里混着已安装产品：只挑里面没人引用的子目录再看
                added += ScanChildren(g, apps, dir, max - added);
                if (max <= 0) return;
            }
        }

        private static int ScanChildren(OrphanGroup g, List<Installed> apps, string dir, int max)
        {
            if (max <= 0) return 0;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return 0; }
            int added = 0;
            string owner;
            for (int i = 0; i < subs.Length && added < max; i++)
            {
                if (OutOfBudget()) return added;
                string sub = subs[i];
                string n = Path.GetFileName(sub);
                if (Skip(n)) continue;
                if (Guard.IsWhitelisted(sub)) continue;
                int strength = RefStrength(sub, apps, out owner);
                if (strength >= 2) continue;
                if (!HasUninstaller(sub))
                {
                    if (IsCopyDir(sub))
                    {
                        string cl = Path.GetFileName(dir) + "\\" + n;
                        if (AddCopy(g, dir, sub, cl)) added++;
                    }
                    continue;
                }
                string label = Path.GetFileName(dir) + "\\" + n;
                if (strength == 1)
                {
                    if (AddWeak(g, dir, sub, label, owner)) added++;
                    continue;
                }
                if (Evaluate(g, dir, sub, label, added)) added++;
            }
            return added;
        }

        /// <summary>只看本层的最近写入时间，用来快速排除明显在用的目录。</summary>
        private static DateTime ShallowNewest(string dir)
        {
            DateTime newest = DateTime.MinValue;
            try { newest = Directory.GetLastWriteTime(dir); }
            catch (Exception) { return newest; }
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return newest; }
            for (int i = 0; i < entries.Length && i < 400; i++)
            {
                try
                {
                    DateTime t = File.GetLastWriteTime(entries[i]);
                    if (t > newest) newest = t;
                }
                catch (Exception) { }
            }
            return newest;
        }

        /// <summary>判断目录名是否是同级目录的“副本”：带数字后缀、副本、copy、(2) 等。</summary>
        private static bool IsCopyDir(string dir)
        {
            string name = Path.GetFileName(dir);
            string parent = Path.GetDirectoryName(dir);
            if (name.Length < 3 || parent == null) return false;
            string baseName = "";
            if (name.EndsWith(Lang.T("副本"))) baseName = name.Substring(0, name.Length - 2);
            else if (name.EndsWith("(2)") || name.EndsWith("（2）")) baseName = name.Substring(0, name.Length - 3);
            else
            {
                int cut = name.Length;
                while (cut > 0 && char.IsDigit(name[cut - 1])) cut--;
                if (cut > 0 && cut < name.Length) baseName = name.Substring(0, cut);
            }
            baseName = baseName.Trim();
            if (baseName.Length < 3) return false;
            if (baseName.ToLowerInvariant() == name.ToLowerInvariant()) return false;
            string sibling = Path.Combine(parent, baseName);
            return Directory.Exists(sibling);
        }

        /// <summary>加入“升级副本目录”残留（同一软件的正规目录还在，这一份没人引用）。</summary>
        private static bool AddCopy(OrphanGroup g, string root, string dir, string label)
        {
            long size = 0;
            try { size = FileUtil.SizeOf(dir); }
            catch (Exception) { }
            if (size < MinBytes) return false;
            string norm = Guard.Norm(dir);
            for (int i = 0; i < g.Items.Count; i++)
            {
                if (Guard.Norm(g.Items[i].Target) == norm) return false;
            }
            DateTime newest = DateTime.MinValue;
            try { newest = ShallowNewest(dir); }
            catch (Exception) { }
            OrphanItem it = new OrphanItem();
            it.GroupId = g.Id;
            it.Kind = "dir";
            it.Name = label;
            it.Target = dir;
            it.Detail = root + Lang.T(" 下 · 升级/安装留下的副本目录，同级的正规目录仍在安装 · 无程序引用") +
                (newest > DateTime.MinValue ? Lang.T(" · 最近改动 ") + newest.ToString("yyyy-MM-dd") : "");
            it.Bytes = size;
            it.AdminNeeded = !Native.IsAdmin();
            g.Items.Add(it);
            return true;
        }

        /// <summary>判定一个目录是否为残留，命中则加入清单并返回 true。</summary>
        private static bool Evaluate(OrphanGroup g, string root, string dir, string label, int added)
        {
            // 判据只看“应用列表里有没有它”：带着卸载程序的目录没必要再按住时间条件猜
            if (!HasUninstaller(dir)) return false;
            DateTime newest;
            try { newest = ShallowNewest(dir); }
            catch (Exception) { newest = DateTime.MinValue; }
            long size = 0;
            try { size = FileUtil.SizeOf(dir); }
            catch (Exception) { }
            if (size < MinBytes) return false;

            string normTarget = Guard.Norm(dir);
            for (int i = 0; i < g.Items.Count; i++)
            {
                if (Guard.Norm(g.Items[i].Target) == normTarget) return false;
            }

            OrphanItem it = new OrphanItem();
            it.GroupId = g.Id;
            it.Kind = "dir";
            it.Name = label;
            it.Target = dir;
            it.Detail = root + Lang.T(" 下 · 应用列表里没有对应安装记录（按“设置-应用”核对）") +
                (newest > DateTime.MinValue ? Lang.T(" · 最近改动 ") + newest.ToString("yyyy-MM-dd") : "");
            it.Bytes = size;
            it.AdminNeeded = !Native.IsAdmin();
            g.Items.Add(it);
            return true;
        }

        /// <summary>同名软件仍在别处安装的目录：只登记、不默认勾选，避免误删正常软件。</summary>
        private static bool AddWeak(OrphanGroup g, string root, string dir, string label, string owner)
        {
            long size = 0;
            try { size = FileUtil.SizeOf(dir); }
            catch (Exception) { }
            if (size < MinBytes) return false;
            string normTarget = Guard.Norm(dir);
            for (int i = 0; i < g.Items.Count; i++)
            {
                if (Guard.Norm(g.Items[i].Target) == normTarget) return false;
            }
            OrphanItem it = new OrphanItem();
            it.GroupId = g.Id;
            it.Kind = "dir";
            it.Name = label;
            it.Target = dir;
            it.Weak = true;
            it.Detail = root + Lang.T(" 下 · 同名软件“") + owner + Lang.T("”已在别处安装，此处疑似旧版本副本，请确认后再删");
            it.Bytes = size;
            it.AdminNeeded = !Native.IsAdmin();
            g.Items.Add(it);
            return true;
        }

        private static bool Skip(string name)
        {
            string low = name.ToLowerInvariant();
            // $Recycle.Bin 这类系统隐藏夹永远不该被当成“软件残留”，
            // 而且安全软件会对它的目录访问做拦截，扫它只会把程序卡住
            if (low.StartsWith("$")) return true;
            for (int i = 0; i < SkipDirs.Length; i++) if (low == SkipDirs[i]) return true;
            return false;
        }

        private static string NameToken(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static bool Referenced(string dir, List<Installed> apps)
        {
            return RefStrength(dir, apps, out _owner) >= 1;
        }

        private static string _owner = "";

        /// <summary>
        /// 2 = 应用列表里的安装路径/图标/卸载命令直接指向这个目录（确定在用，绝不动）
        /// 1 = 同名产品在别处安装（大概率是旧版本副本，列出但不默认勾选）
        /// 0 = 应用列表里查不到任何对应记录（才可能是残留）
        /// 判据只有“设置-应用”里的安装清单，不看修改时间——时间新不代表在用，时间旧也不代表被卸载。
        /// </summary>
        private static int RefStrength(string dir, List<Installed> apps, out string owner)
        {
            owner = "";
            string d = Guard.Norm(dir);
            string leaf = Path.GetFileName(dir).ToLowerInvariant();
            string token = NameToken(Path.GetFileName(dir));
            for (int i = 0; i < apps.Count; i++)
            {
                Installed a = apps[i];
                string loc = Guard.Norm(a.Location);
                if (loc.Length > 0)
                {
                    if (loc == d) { owner = a.Name; return 2; }
                    if (loc.StartsWith(d + "\\")) { owner = a.Name; return 2; }
                    if (d.StartsWith(loc + "\\")) { owner = a.Name; return 2; }
                }
                string icon = Guard.Norm(a.Icon);
                if (icon.Length > 0 && (icon.Contains(d + "\\") || icon.StartsWith(d))) { owner = a.Name; return 2; }
                string un = Guard.Norm(a.Uninstall);
                if (un.Length > 0 && (un.Contains(d + "\\") || un.StartsWith(d))) { owner = a.Name; return 2; }
                if (token.Length >= 4 && NameToken(a.Name) == token) { owner = a.Name; return 2; }
            }
            // 弱证据：这个目录名作为“产品名”出现在已安装程序的名字或路径里（360zip、PotPlayer 这类）
            if (leaf.Length >= 3)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    Installed a = apps[i];
                    if (NameToken(a.Name).Contains(token) && token.Length >= 3) { owner = a.Name; return 1; }
                    if (Guard.Norm(a.Location).Contains(leaf) && leaf.Length >= 4) { owner = a.Name; return 1; }
                    if (Guard.Norm(a.Icon).Contains(leaf) && leaf.Length >= 4) { owner = a.Name; return 1; }
                    if (Guard.Norm(a.Uninstall).Contains(leaf) && leaf.Length >= 4) { owner = a.Name; return 1; }
                }
            }
            return 0;
        }

        // ---------- 无效卸载注册表项 ----------

        private static readonly string[] SoftRoots = new string[]
        {
            @"SOFTWARE",
            @"SOFTWARE\WOW6432Node"
        };

        private static readonly string[] SoftSkipKeys = new string[]
        {
            "microsoft", "windows", "windows nt", "classes", "policies", "clients",
            "registeredapplications", "wow6432node", "odbc", "registeredapplications"
        };

        /// <summary>注册表里仍指向“已不存在目录”的软件键：键值里出现孤儿目录路径即判定。</summary>
        private static OrphanGroup ScanRegResidue(OrphanGroup dirs, Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "regres";
            g.Name = Lang.T("残留注册表键");
            g.Desc = Lang.T("注册表值里还写着已删软件目录的键（直接删除，不留备份）");
            R(report, Lang.T("扫描残留注册表键..."));

            List<string> targets = new List<string>();
            for (int i = 0; i < dirs.Items.Count; i++)
            {
                OrphanItem d = dirs.Items[i];
                if (d.Kind != "dir") continue;
                targets.Add(Guard.Norm(d.Target));
            }
            if (targets.Count == 0) return g;

            ScanSoftHive(Registry.LocalMachine, "HKLM", @"SOFTWARE", targets, g);
            ScanSoftHive(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node", targets, g);
            ScanSoftHive(Registry.CurrentUser, "HKCU", @"SOFTWARE", targets, g);
            return g;
        }

        private static void ScanSoftHive(RegistryKey hive, string hiveName, string rootPath, List<string> targets, OrphanGroup g)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(rootPath))
                {
                    if (root == null) return;
                    string[] vendors = root.GetSubKeyNames();
                    for (int i = 0; i < vendors.Length && g.Items.Count < 80; i++)
                    {
                        string v = vendors[i];
                        if (IsSoftSkip(v)) continue;
                        ScanSoftKey(root, hiveName, rootPath, v, targets, g, true);
                    }
                }
            }
            catch (Exception) { }
        }

        private static bool IsSoftSkip(string name)
        {
            string low = name.ToLowerInvariant();
            for (int i = 0; i < SoftSkipKeys.Length; i++) if (low == SoftSkipKeys[i]) return true;
            return false;
        }

        private static void ScanSoftKey(RegistryKey parent, string hiveName, string rootPath, string name, List<string> targets, OrphanGroup g, bool deep)
        {
            string keyPath = rootPath + "\\" + name;
            try
            {
                using (RegistryKey k = parent.OpenSubKey(name))
                {
                    if (k == null) return;
                    string hit = HitValue(k, targets);
                    if (hit.Length > 0)
                    {
                        OrphanItem it = new OrphanItem();
                        it.GroupId = g.Id;
                        it.Kind = "reg";
                        it.Name = name;
                        it.Target = hiveName + "\\" + keyPath;
                        it.Detail = Lang.T("键值指向已不存在的目录：") + hit;
                        it.Hive = hiveName;
                        it.KeyPath = keyPath;
                        it.AdminNeeded = !Native.IsAdmin();
                        g.Items.Add(it);
                        return;
                    }
                    if (!deep) return;
                    string[] subs;
                    try { subs = k.GetSubKeyNames(); }
                    catch (Exception) { return; }
                    for (int i = 0; i < subs.Length && g.Items.Count < 80; i++)
                    {
                        string sn = subs[i];
                        if (sn.ToLowerInvariant() == "uninstall") continue;
                        ScanSoftKey(k, hiveName, keyPath, sn, targets, g, false);
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>在键值里找孤儿目录路径，返回命中的值片段。</summary>
        private static string HitValue(RegistryKey k, List<string> targets)
        {
            string[] names;
            try { names = k.GetValueNames(); }
            catch (Exception) { return ""; }
            for (int i = 0; i < names.Length; i++)
            {
                string s;
                try
                {
                    object val = k.GetValue(names[i]);
                    if (val == null) continue;
                    if (!(val is string)) continue;
                    s = (string)val;
                }
                catch (Exception) { continue; }
                if (s.Length < 4) continue;
                string norm = Guard.Norm(s);
                for (int j = 0; j < targets.Count; j++)
                {
                    string t = targets[j];
                    if (t.Length < 4) continue;
                    if (norm.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return s;
                }
            }
            return "";
        }

        private static OrphanGroup ScanRegistry(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "reg";
            g.Name = Lang.T("无效卸载项");
            g.Desc = Lang.T("注册表里还在、但程序早已不存在的卸载记录（直接删除，不留备份）");
            R(report, Lang.T("扫描无效卸载注册表项..."));

            ReadReg(Registry.LocalMachine, "HKLM", UninstallRoots[0], g);
            ReadReg(Registry.LocalMachine, "HKLM", UninstallRoots[1], g);
            ReadReg(Registry.CurrentUser, "HKCU", UninstallRoots[0], g);
            return g;
        }

        private static void ReadReg(RegistryKey hive, string hiveName, string rootPath, OrphanGroup g)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(rootPath))
                {
                    if (root == null) return;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(names[i]))
                            {
                                if (k == null) continue;
                                string display = Str(k.GetValue("DisplayName"));
                                if (display.Length == 0) continue;
                                string uninstall = Str(k.GetValue("UninstallString"));
                                if (uninstall.Length == 0) continue;
                                if (uninstall.ToLowerInvariant().Contains("msiexec")) continue;
                                string exe = ExtractExe(uninstall);
                                if (exe.Length == 0) continue;
                                if (File.Exists(exe)) continue;

                                string loc = Str(k.GetValue("InstallLocation")).Trim('"');
                                bool locGone = loc.Length == 0 || !Directory.Exists(loc);
                                string icon = IconPath(Str(k.GetValue("DisplayIcon")));
                                bool iconGone = icon.Length == 0 || !File.Exists(icon);

                                if (!locGone || !iconGone) continue;

                                OrphanItem it = new OrphanItem();
                                it.GroupId = g.Id;
                                it.Kind = "reg";
                                it.Name = display;
                                it.Hive = hiveName;
                                it.KeyPath = rootPath + "\\" + names[i];
                                it.Target = hiveName + "\\" + it.KeyPath;
                                it.Detail = Lang.T("卸载命令指向的文件已不存在");
                                it.AdminNeeded = hiveName == "HKLM" && !Native.IsAdmin();
                                g.Items.Add(it);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static string IconPath(string icon)
        {
            if (icon.Length == 0) return "";
            string p = icon.Trim();
            int comma = p.LastIndexOf(',');
            if (comma > 0) p = p.Substring(0, comma);
            return p.Trim('"');
        }

        /// <summary>从命令行里取出可执行文件路径（支持带引号、带参数、rundll32 等）。</summary>
        private static string ExtractExe(string cmd)
        {
            string s = cmd.Trim();
            if (s.Length == 0) return "";
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return Guard.Expand(s.Substring(1, end - 1));
                return "";
            }
            int space = s.IndexOf(' ');
            string first = space > 0 ? s.Substring(0, space) : s;
            if (first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Guard.Expand(first);
            // “cmd /c xxx.exe”之类
            if (first.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) || first.EndsWith("rundll32.exe", StringComparison.OrdinalIgnoreCase))
            {
                string rest = space > 0 ? s.Substring(space + 1).Trim() : "";
                if (rest.StartsWith("/c ")) rest = rest.Substring(3).Trim();
                if (rest.StartsWith("\""))
                {
                    int end = rest.IndexOf('"', 1);
                    if (end > 1) return Guard.Expand(rest.Substring(1, end - 1));
                }
                int sp2 = rest.IndexOf(' ');
                string f2 = sp2 > 0 ? rest.Substring(0, sp2) : rest;
                if (f2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Guard.Expand(f2);
                return "";
            }
            return "";
        }

        // ---------- 失效快捷方式 ----------

        private static OrphanGroup ScanShortcuts(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "lnk";
            g.Name = Lang.T("失效快捷方式");
            g.Desc = Lang.T("桌面与开始菜单里指向已不存在程序的快捷方式（删除时移入回收站）");
            R(report, Lang.T("扫描失效快捷方式..."));

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string desktopCommon = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string startUser = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            string startCommon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs");

            ScanLnkDir(g, desktop, 0, 3);
            ScanLnkDir(g, desktopCommon, 0, 3);
            ScanLnkDir(g, startUser, 0, 4);
            ScanLnkDir(g, startCommon, 0, 4);
            return g;
        }

        private static void ScanLnkDir(OrphanGroup g, string dir, int depth, int maxDepth)
        {
            if (depth > maxDepth || !Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.lnk"); }
            catch (Exception) { files = new string[0]; }
            for (int i = 0; i < files.Length; i++)
            {
                string target = ShortcutTarget(files[i]);
                if (target.Length == 0) continue;
                target = Guard.Expand(target);
                if (target.Length < 4) continue;
                if (File.Exists(target) || Directory.Exists(target)) continue;
                if (target.ToLowerInvariant().Contains("%")) continue;
                OrphanItem it = new OrphanItem();
                it.GroupId = g.Id;
                it.Kind = "lnk";
                it.Name = Path.GetFileNameWithoutExtension(files[i]);
                it.Target = files[i];
                string shortTarget = target.Length > 70 ? "..." + target.Substring(target.Length - 66) : target;
                it.Detail = Lang.T("指向 ") + shortTarget;
                it.Bytes = 0;
                it.AdminNeeded = !Native.IsAdmin() && dir.Contains("ProgramData");
                g.Items.Add(it);
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < subs.Length; i++) ScanLnkDir(g, subs[i], depth + 1, maxDepth);
        }

        private static string ShortcutTarget(string lnk)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type scType = sc.GetType();
                object v = scType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                return v == null ? "" : v.ToString();
            }
            catch (Exception) { return ""; }
        }

        // ---------- 失效启动项 ----------

        private static OrphanGroup ScanStartup(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "startup";
            g.Name = Lang.T("失效启动项");
            g.Desc = Lang.T("指向已不存在程序的启动项（禁用前会备份，可从“启动项管理”还原）");
            R(report, Lang.T("扫描失效启动项..."));
            List<StartupItem> items = Startup.List();
            for (int i = 0; i < items.Count; i++)
            {
                StartupItem s = items[i];
                if (s.IsFile) continue;
                if (s.IsService || s.IsTask) continue;
                string exe = ExtractExe(s.Command);
                if (exe.Length == 0) continue;
                if (File.Exists(exe)) continue;
                OrphanItem it = new OrphanItem();
                it.GroupId = g.Id;
                it.Kind = "startup";
                it.Name = s.Name;
                it.Target = s.Source + " · " + s.Command;
                it.Detail = Lang.T("启动命令指向的文件不存在");
                it.AdminNeeded = s.Machine && !Native.IsAdmin();
                it.Data = s;
                g.Items.Add(it);
            }
            return g;
        }

        // ---------- 空目录 / 空文件 / 空注册表键 ----------

        private static string[] EmptyRoots()
        {
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            List<string> ok = new List<string>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] != null && roots[i].Length > 0 && Directory.Exists(roots[i]) && !ok.Contains(roots[i])) ok.Add(roots[i]);
            }
            return ok.ToArray();
        }

        private static bool SkipEmptyName(string name)
        {
            string n = name.ToLowerInvariant();
            return n == "system volume information" || n == "$recycle.bin" || n == ".git" ||
                   n == "node_modules" || n == "desktop.ini" || n == ".gitkeep" || n == "thumbs.db";
        }

        private static int[] VisitBudget = new int[] { 20000 };

        /// <summary>返回该目录是否（在忽略空子目录后）完全为空；顺带收集空子目录。</summary>
        private static bool WalkEmpty(string dir, int depth, List<string> found, int limit)
        {
            if (depth > 4 || found.Count >= limit) return false;
            if (VisitBudget[0]-- <= 0) return false;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return false; }
            bool allEmpty = true;
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i];
                try
                {
                    FileAttributes attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) { allEmpty = false; continue; }
                    if ((attr & FileAttributes.Directory) != FileAttributes.Directory) { allEmpty = false; continue; }
                    if (SkipEmptyName(Path.GetFileName(e))) { allEmpty = false; continue; }
                    if (WalkEmpty(e, depth + 1, found, limit))
                    {
                        if (found.Count < limit) found.Add(e);
                    }
                    else
                    {
                        allEmpty = false;
                    }
                }
                catch (Exception) { allEmpty = false; }
            }
            return allEmpty;
        }

        private static OrphanGroup ScanEmptyDirs(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "empty";
            g.Name = Lang.T("空目录");
            g.Desc = Lang.T("桌面、文档、下载、图片、音乐、视频里不含任何文件的目录（删除不影响任何数据）");
            R(report, Lang.T("扫描空目录..."));
            VisitBudget[0] = 6000;
            string[] roots = EmptyRoots();
            List<string> found = new List<string>();
            for (int i = 0; i < roots.Length && found.Count < 400; i++)
            {
                string root = roots[i];
                string[] entries;
                try { entries = Directory.GetDirectories(root); }
                catch (Exception) { continue; }
                for (int j = 0; j < entries.Length && found.Count < 400; j++)
                {
                    if (SkipEmptyName(Path.GetFileName(entries[j]))) continue;
                    try
                    {
                        if ((File.GetAttributes(entries[j]) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    }
                    catch (Exception) { continue; }
                    if (WalkEmpty(entries[j], 0, found, 400)) found.Add(entries[j]);
                }
            }
            // 只保留最外层：父目录本身也是空的就不用再单列子目录
            List<string> top = new List<string>();
            Dictionary<string, bool> set = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < found.Count; i++) set[Guard.Norm(found[i])] = true;
            for (int i = 0; i < found.Count; i++)
            {
                string parent = Path.GetDirectoryName(found[i]);
                if (parent != null && set.ContainsKey(Guard.Norm(parent))) continue;
                top.Add(found[i]);
            }
            found = top;
            for (int i = 0; i < found.Count; i++)
            {
                OrphanItem it = new OrphanItem();
                it.GroupId = g.Id;
                it.Kind = "emptyDir";
                it.Name = Path.GetFileName(found[i]);
                it.Target = found[i];
                it.Detail = found[i];
                it.Bytes = 0;
                g.Items.Add(it);
            }
            R(report, Lang.T("空目录：") + g.Items.Count + Lang.T(" 项"));
            return g;
        }

        private static OrphanGroup ScanEmptyFiles(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "emptyfile";
            g.Name = Lang.T("空文件");
            g.Desc = Lang.T("桌面、文档、下载、图片、音乐、视频里 7 天前创建、大小为 0 的文件");
            R(report, Lang.T("扫描空文件..."));
            VisitBudget[0] = 6000;
            string[] roots = EmptyRoots();
            DateTime cutoff = DateTime.Now.AddDays(-7);
            for (int r = 0; r < roots.Length && g.Items.Count < 400; r++)
            {
                CollectEmptyFiles(g, roots[r], cutoff, 0, report);
            }
            R(report, Lang.T("空文件：") + g.Items.Count + Lang.T(" 项"));
            return g;
        }

        private static void CollectEmptyFiles(OrphanGroup g, string dir, DateTime cutoff, int depth, Action<string> report)
        {
            if (depth > 4 || g.Items.Count >= 400) return;
            if (VisitBudget[0]-- <= 0) return;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { files = new string[0]; }
            for (int i = 0; i < files.Length && g.Items.Count < 400; i++)
            {
                string f = files[i];
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (n == "desktop.ini" || n == ".gitkeep" || n == "thumbs.db") continue;
                try
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length != 0 || fi.LastWriteTime >= cutoff) continue;
                    if (!Guard.CanDelete(f, true)) continue;
                    OrphanItem it = new OrphanItem();
                    it.GroupId = g.Id;
                    it.Kind = "emptyFile";
                    it.Name = Path.GetFileName(f);
                    it.Target = f;
                    it.Detail = f;
                    it.Bytes = 0;
                    g.Items.Add(it);
                }
                catch (Exception) { }
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < subs.Length; i++)
            {
                try
                {
                    FileAttributes attr = File.GetAttributes(subs[i]);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                }
                catch (Exception) { continue; }
                if (SkipEmptyName(Path.GetFileName(subs[i]))) continue;
                CollectEmptyFiles(g, subs[i], cutoff, depth + 1, report);
            }
        }

        private static readonly string[] RegSkipNames = new string[]
        {
            "microsoft", "classes", "policies", "wow6432node", "registeredapplications", "clients",
            "licensedposix", "microsoft\\windows", "microsoft\\office", "microsoft\\internet"
        };

        private static OrphanGroup ScanEmptyRegKeys(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "emptyreg";
            g.Name = Lang.T("空注册表键");
            g.Desc = Lang.T("HKCU\\Software 下既没有值也没有子键的空壳键（删除前自动导出备份，软件会按需重建）");
            R(report, Lang.T("扫描空注册表键..."));
            try
            {
                using (RegistryKey soft = Registry.CurrentUser.OpenSubKey("Software"))
                {
                    if (soft != null)
                    {
                        string[] names = soft.GetSubKeyNames();
                        for (int i = 0; i < names.Length && g.Items.Count < 200; i++)
                        {
                            string vendor = names[i];
                            if (vendor.ToLowerInvariant() == "agcleaner") continue;
                            using (RegistryKey vk = soft.OpenSubKey(vendor))
                            {
                                if (vk == null) continue;
                                if (vk.ValueCount == 0 && vk.SubKeyCount == 0)
                                {
                                    AddEmptyReg(g, "Software\\" + vendor, vendor);
                                    continue;
                                }
                                string[] subs = vk.GetSubKeyNames();
                                for (int j = 0; j < subs.Length && g.Items.Count < 200; j++)
                                {
                                    using (RegistryKey sk = vk.OpenSubKey(subs[j]))
                                    {
                                        if (sk == null) continue;
                                        if (sk.ValueCount == 0 && sk.SubKeyCount == 0)
                                        {
                                            AddEmptyReg(g, "Software\\" + vendor + "\\" + subs[j], vendor + "\\" + subs[j]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            R(report, Lang.T("空注册表键：") + g.Items.Count + Lang.T(" 项"));
            return g;
        }

        private static void AddEmptyReg(OrphanGroup g, string path, string label)
        {
            OrphanItem it = new OrphanItem();
            it.GroupId = g.Id;
            it.Kind = "reg";
            it.Name = label;
            it.Target = "HKCU\\" + path;
            it.Detail = Lang.T("空键，无任何值与子键");
            it.Hive = "HKCU";
            it.KeyPath = path;
            g.Items.Add(it);
        }

        // ---------- 执行 ----------

        public static long Execute(List<OrphanItem> items, out List<string> log, out int errors)
        {
            return Execute(items, out log, out errors, null);
        }

        public static long Execute(List<OrphanItem> items, out List<string> log, out int errors, StepProgress prog)
        {
            log = new List<string>();
            errors = 0;
            long freed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                OrphanItem it = items[i];
                if (prog != null) prog(i * 100 / Math.Max(1, items.Count), Lang.T("正在处理 ") + (i + 1) + "/" + items.Count + " · " + it.Name);
                try
                {
                    if (it.Kind == "dir")
                    {
                        if (!CanRemoveDir(it.Target)) { errors++; log.Add(Lang.T("跳过（层级过浅或属系统目录）：") + it.Target); continue; }
                        long size = FileUtil.SizeOf(it.Target);
                        List<string> one = new List<string>();
                        one.Add(it.Target);
                        if (Native.Recycle(one))
                        {
                            freed += size;
                            log.Add(Lang.T("已移入回收站：") + it.Target);
                            OpLog.Write("orphan", Lang.T("回收孤儿目录 ") + it.Target, size);
                        }
                        else
                        {
                            errors++;
                            log.Add(Lang.T("回收站拒绝（可能被占用）：") + it.Target);
                        }
                    }
                    else if (it.Kind == "emptyDir")
                    {
                        if (!Guard.CanDelete(it.Target, true)) { errors++; log.Add(Lang.T("跳过（受保护）：") + it.Target); continue; }
                        try
                        {
                            Directory.Delete(it.Target, false);
                            if (Directory.Exists(it.Target)) { errors++; log.Add(Lang.T("目录仍存在：") + it.Target); continue; }
                            log.Add(Lang.T("已删除空目录：") + it.Target);
                            OpLog.Write("orphan", Lang.T("删除空目录 ") + it.Target, 0);
                        }
                        catch (Exception ex) { errors++; log.Add(Lang.T("删除失败：") + it.Target + " · " + ex.Message); }
                    }
                    else if (it.Kind == "emptyFile")
                    {
                        try
                        {
                            File.Delete(it.Target);
                            if (File.Exists(it.Target)) { errors++; log.Add(Lang.T("文件仍存在：") + it.Target); continue; }
                            log.Add(Lang.T("已删除空文件：") + it.Target);
                            OpLog.Write("orphan", Lang.T("删除空文件 ") + it.Target, 0);
                        }
                        catch (Exception ex) { errors++; log.Add(Lang.T("删除失败：") + it.Target + " · " + ex.Message); }
                    }
                    else if (it.Kind == "reg")
                    {
                        if (!DeleteRegKey(it, log)) { errors++; continue; }
                        log.Add(Lang.T("已删除注册表项：") + it.Target);
                        OpLog.Write("orphan", Lang.T("删除无效卸载项 ") + it.Target, 0);
                    }
                    else if (it.Kind == "lnk")
                    {
                        List<string> one = new List<string>();
                        one.Add(it.Target);
                        bool ok = Native.Recycle(one);
                        if (!ok)
                        {
                            try { File.Delete(it.Target); ok = !File.Exists(it.Target); }
                            catch (Exception) { ok = false; }
                        }
                        if (!ok) { errors++; log.Add(Lang.T("删除失败：") + it.Target); continue; }
                        log.Add(Lang.T("已删除快捷方式：") + it.Target);
                        OpLog.Write("orphan", Lang.T("删除失效快捷方式 ") + it.Target, 0);
                    }
                    else if (it.Kind == "startup")
                    {
                        StartupItem s = it.Data as StartupItem;
                        if (s == null) { errors++; continue; }
                        string msg;
                        if (!Startup.Disable(s, out msg)) { errors++; log.Add(Lang.T("禁用失败：") + it.Name + " · " + msg); continue; }
                        log.Add(Lang.T("已禁用启动项：") + it.Name);
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add(Lang.T("异常：") + it.Name + " · " + ex.Message);
                }
            }
            if (prog != null) prog(100, Lang.T("处理完成"));
            return freed;
        }

        private static bool CanRemoveDir(string path)
        {
            string p = Guard.Norm(path);
            string[] parts = p.Split('\\');
            if (parts.Length < 3) return false;
            string name = parts[parts.Length - 1];
            if (Skip(name)) return false;
            string pf = Guard.Norm(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string pf86 = Guard.Norm(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string pd = Guard.Norm(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            string la = Guard.Norm(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            string ra = Guard.Norm(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            string lap = Guard.Norm(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            if (p == pf || p == pf86 || p == pd || p == la || p == ra || p == lap) return false;
            if (p.Contains("\\windows\\")) return false;
            return true;
        }

        private static bool DeleteRegKey(OrphanItem it, List<string> log)
        {
            try
            {
                // 残留项直接删除，不做备份——备份本身就是新的垃圾

                RegistryKey hive = it.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                hive.DeleteSubKeyTree(it.KeyPath, false);
                if (hive.OpenSubKey(it.KeyPath) != null) { log.Add(Lang.T("注册表项仍在，可能权限不足：") + it.Target); return false; }
                log.Add(Lang.T("已删除注册表项：") + it.Target);
                return true;
            }
            catch (Exception ex)
            {
                log.Add(Lang.T("注册表删除失败：") + it.Name + " · " + ex.Message);
                return false;
            }
        }

        private static string RegexSafe(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length && i < 40; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            if (sb.Length == 0) sb.Append("key");
            return sb.ToString();
        }

        private static void RunReg(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "reg.exe";
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(8000);
                }
            }
            catch (Exception) { }
        }
    }
}
