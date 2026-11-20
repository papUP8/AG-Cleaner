using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace CCDesk.Core
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
            "modifiable", "temp", "packages", "package cache", "installer", "ccdesk", "public",
            "system", "config", "drivers", "desktop.ini", "dotnet", "reference assemblies",
            "windows defender advanced threat protection", "comms", "connecteddevicesplatform",
            "ebwebview", "webview", "edgeupdate", "edgecore", "microsoft edge", "python"
        };

        private const int MinAgeDays = 180;
        private const long MinBytes = 30 * 1024 * 1024;

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
            List<OrphanGroup> groups = new List<OrphanGroup>();
            List<Installed> apps = ReadInstalled();
            R(report, "读取已安装程序索引：" + apps.Count + " 条");
            groups.Add(ScanDirs(apps, report));
            groups.Add(ScanRegistry(report));
            groups.Add(ScanShortcuts(report));
            groups.Add(ScanStartup(report));
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

        private static OrphanGroup ScanDirs(List<Installed> apps, Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "dir";
            g.Name = "孤儿安装目录";
            g.Desc = "已卸载软件留下、且没有被任何已安装程序引用的目录（默认不勾选，逐个确认）";
            g.AdminOnly = true;
            R(report, "扫描孤儿安装目录...");

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ra = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string lap = Path.Combine(la, "Programs");

            ScanRoot(g, apps, pf, 90);
            ScanRoot(g, apps, pf86, 90);
            ScanRoot(g, apps, lap, 60);
            ScanRoot(g, apps, pd, 60);
            ScanRoot(g, apps, la, 40);
            ScanRoot(g, apps, ra, 40);
            return g;
        }

        private static void ScanRoot(OrphanGroup g, List<Installed> apps, string root, int max)
        {
            if (root.Length == 0 || !Directory.Exists(root)) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch (Exception) { return; }
            DateTime cutoff = DateTime.Now.AddDays(-MinAgeDays);
            int added = 0;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (added >= max) return;
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (Skip(name)) continue;
                if (Guard.IsWhitelisted(dir)) continue;
                if (Referenced(dir, apps)) continue;
                // 目录时间戳常常滞后，用里面最近一次文件写入时间判断是否还在使用
                DateTime newest;
                try { newest = NewestWrite(dir); }
                catch (Exception) { continue; }
                if (newest > cutoff) continue;
                long size = 0;
                try { size = FileUtil.SizeOf(dir); }
                catch (Exception) { }
                if (size < MinBytes) continue;
                OrphanItem it = new OrphanItem();
                it.GroupId = g.Id;
                it.Kind = "dir";
                it.Name = name;
                it.Target = dir;
                it.Detail = root + " 下 · 超过 " + MinAgeDays + " 天未改动 · 无已安装程序引用";
                it.Bytes = size;
                it.AdminNeeded = !Native.IsAdmin();
                g.Items.Add(it);
                added++;
            }
        }

        private static bool Skip(string name)
        {
            string low = name.ToLowerInvariant();
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
            string d = Guard.Norm(dir);
            string token = NameToken(Path.GetFileName(dir));
            for (int i = 0; i < apps.Count; i++)
            {
                Installed a = apps[i];
                string loc = Guard.Norm(a.Location);
                if (loc.Length > 0)
                {
                    if (loc == d) return true;
                    if (loc.StartsWith(d + "\\")) return true;
                    if (d.StartsWith(loc + "\\")) return true;
                }
                string icon = Guard.Norm(a.Icon);
                if (icon.Length > 0 && (icon.Contains(d + "\\") || icon.StartsWith(d))) return true;
                string un = Guard.Norm(a.Uninstall);
                if (un.Length > 0 && (un.Contains(d + "\\") || un.StartsWith(d))) return true;
                if (token.Length >= 4 && NameToken(a.Name) == token) return true;
            }
            return false;
        }

        // ---------- 无效卸载注册表项 ----------

        private static OrphanGroup ScanRegistry(Action<string> report)
        {
            OrphanGroup g = new OrphanGroup();
            g.Id = "reg";
            g.Name = "无效卸载项";
            g.Desc = "注册表里还在、但程序早已不存在的卸载记录（删除前自动导出 .reg 备份）";
            R(report, "扫描无效卸载注册表项...");

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
                                it.Detail = "卸载命令指向的文件已不存在";
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
            g.Name = "失效快捷方式";
            g.Desc = "桌面与开始菜单里指向已不存在程序的快捷方式（删除时移入回收站）";
            R(report, "扫描失效快捷方式...");

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
                it.Detail = "指向 " + shortTarget;
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
            g.Name = "失效启动项";
            g.Desc = "指向已不存在程序的启动项（禁用前会备份，可从“启动项管理”还原）";
            R(report, "扫描失效启动项...");
            List<StartupItem> items = Startup.List();
            for (int i = 0; i < items.Count; i++)
            {
                StartupItem s = items[i];
                if (s.IsFile) continue;
                string exe = ExtractExe(s.Command);
                if (exe.Length == 0) continue;
                if (File.Exists(exe)) continue;
                OrphanItem it = new OrphanItem();
                it.GroupId = g.Id;
                it.Kind = "startup";
                it.Name = s.Name;
                it.Target = s.Source + " · " + s.Command;
                it.Detail = "启动命令指向的文件不存在";
                it.AdminNeeded = s.Machine && !Native.IsAdmin();
                it.Data = s;
                g.Items.Add(it);
            }
            return g;
        }

        // ---------- 执行 ----------

        public static long Execute(List<OrphanItem> items, out List<string> log, out int errors)
        {
            log = new List<string>();
            errors = 0;
            long freed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                OrphanItem it = items[i];
                try
                {
                    if (it.Kind == "dir")
                    {
                        if (!CanRemoveDir(it.Target)) { errors++; log.Add("跳过（层级过浅或属系统目录）：" + it.Target); continue; }
                        long size = FileUtil.SizeOf(it.Target);
                        List<string> one = new List<string>();
                        one.Add(it.Target);
                        if (Native.Recycle(one))
                        {
                            freed += size;
                            log.Add("已移入回收站：" + it.Target);
                            OpLog.Write("orphan", "回收孤儿目录 " + it.Target, size);
                        }
                        else
                        {
                            errors++;
                            log.Add("回收站拒绝（可能被占用）：" + it.Target);
                        }
                    }
                    else if (it.Kind == "reg")
                    {
                        if (!DeleteRegKey(it, log)) { errors++; continue; }
                        log.Add("已删除注册表项：" + it.Target);
                        OpLog.Write("orphan", "删除无效卸载项 " + it.Target, 0);
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
                        if (!ok) { errors++; log.Add("删除失败：" + it.Target); continue; }
                        log.Add("已删除快捷方式：" + it.Target);
                        OpLog.Write("orphan", "删除失效快捷方式 " + it.Target, 0);
                    }
                    else if (it.Kind == "startup")
                    {
                        StartupItem s = it.Data as StartupItem;
                        if (s == null) { errors++; continue; }
                        string msg;
                        if (!Startup.Disable(s, out msg)) { errors++; log.Add("禁用失败：" + it.Name + " · " + msg); continue; }
                        log.Add("已禁用启动项：" + it.Name);
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add("异常：" + it.Name + " · " + ex.Message);
                }
            }
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
                string dir = Path.Combine(Store.RootDir, "backup");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string safe = RegexSafe(it.Name);
                string file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + safe + ".reg");
                string full = it.Hive + "\\" + it.KeyPath;
                RunReg("export \"" + full + "\" \"" + file + "\" /y");
                if (!File.Exists(file)) { log.Add("备份失败，已跳过：" + it.Target); return false; }

                RegistryKey hive = it.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                hive.DeleteSubKeyTree(it.KeyPath, false);
                if (hive.OpenSubKey(it.KeyPath) != null) { log.Add("注册表项仍在，可能权限不足：" + it.Target); return false; }
                log.Add("备份：" + Path.GetFileName(file));
                return true;
            }
            catch (Exception ex)
            {
                log.Add("注册表删除失败：" + it.Name + " · " + ex.Message);
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
