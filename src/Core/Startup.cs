using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AGCleaner.Core
{
    public class StartupItem
    {
        public string Name = "";
        public string Command = "";
        public string Source = "";
        public string Group = "user";          // user / sys / task / service
        public bool Enabled = true;
        public bool Machine = false;
        public bool IsFile = false;
        public bool IsTask = false;
        public bool IsService = false;
        public string KeyPath = "";
        public string ValueName = "";
        public string FilePath = "";
        public string HiveName = "HKCU";
        public string ApprovalPath = "";       // StartupApproved 子键
        public string ApprovalName = "";       // StartupApproved 值名
        public string TaskPath = "";           // 计划任务路径
        public string Detail = "";             // 额外说明（启动方式等）

        public string GroupText
        {
            get
            {
                if (Group == "sys") return Lang.T("系统组件");
                if (Group == "task") return Lang.T("计划任务");
                if (Group == "plugin") return Lang.T("自动插件");
                if (Group == "appsvc") return Lang.T("应用软件服务");
                if (Group == "syssvc") return Lang.T("系统关键服务");
                if (Group == "service") return Lang.T("系统服务");
                return Lang.T("用户启动项");
            }
        }
    }

    /// <summary>启动项读取与启停（禁用时备份，可随时还原）。</summary>
    public static class Startup
    {
        private static readonly string[] RunKeys = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };

        public static string BackupDir
        {
            get
            {
                string d = Path.Combine(Store.RootDir, "startup-backup");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        private static string BackupFile { get { return Path.Combine(Store.RootDir, "startup-backup.tsv"); } }

        public static List<StartupItem> List()
        {
            List<StartupItem> items = new List<StartupItem>();

            // 用户与全局 Run
            ReadRunKey(Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "user", false, @"Run", items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "sys", true, @"Run", items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "sys", true, @"Run32", items);

            // 只执行一次的启动项（常留下失败残留）
            ReadRunKey(Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "user", false, @"Run32", items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "sys", true, @"Run32", items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", "sys", true, @"Run32", items);

            // 旧式与策略位置
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\RunServices", "sys", true, @"Run32", items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "sys", true, @"Run32", items);
            ReadRunKey(Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "user", false, @"Run", items);

            ReadStartupFolders(items);
            ReadActiveSetup(items);
            ReadTasks(items);
            ReadServices(items);
            ReadPlugins(items);
            ReadDisabled(items);

            items.Sort(delegate (StartupItem a, StartupItem b)
            {
                int ga = GroupRank(a.Group), gb = GroupRank(b.Group);
                if (ga != gb) return ga - gb;
                if (a.Enabled != b.Enabled) return a.Enabled ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Name = Clean(items[i].Name);
                items[i].Detail = Clean(items[i].Detail);
                items[i].Command = Clean(items[i].Command);
            }
            return items;
        }

        /// <summary>清掉注册表里用来排序的控制字符（常见 \u0001）与多余空白，避免界面出现方框乱码。</summary>
        private static string Clean(string s)
        {
            if (s == null || s.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            bool space = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsControl(c)) continue;
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static int GroupRank(string g)
        {
            if (g == "user") return 0;
            if (g == "sys") return 1;
            if (g == "task") return 2;
            if (g == "plugin") return 3;
            if (g == "appsvc") return 4;
            return 5;
        }

        private static void ReadRunKey(RegistryKey hive, string hiveName, string keyPath, string group, bool machine, string approvalSub, List<StartupItem> items)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(keyPath))
                {
                    if (k == null) return;
                    string[] names = k.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            object val = k.GetValue(names[i]);
                            if (val == null) continue;
                            StartupItem it = new StartupItem();
                            it.Name = names[i];
                            it.Command = val.ToString();
                            it.Machine = machine;
                            it.HiveName = hiveName;
                            it.KeyPath = keyPath;
                            it.ValueName = names[i];
                            it.Group = group;
                            it.Source = (machine ? "HKLM " : "HKCU ") + ShortKey(keyPath);
                            it.ApprovalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + approvalSub;
                            it.ApprovalName = names[i];
                            it.Enabled = !ApprovalDisabled(machine, it.ApprovalPath, it.ApprovalName);
                            if (it.Name.Length == 0) it.Name = Lang.T("未命名启动项");
                            items.Add(it);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static string ShortKey(string keyPath)
        {
            int i = keyPath.LastIndexOf('\\');
            string tail = i >= 0 ? keyPath.Substring(i + 1) : keyPath;
            if (keyPath.IndexOf("WOW6432Node", StringComparison.OrdinalIgnoreCase) >= 0) return tail + Lang.T(" (32位)");
            return tail;
        }

        /// <summary>读取系统“启动应用已被禁用”的标记（任务管理器写在这里）。</summary>
        private static bool ApprovalDisabled(bool machine, string subKey, string valueName)
        {
            try
            {
                RegistryKey hive = machine ? Registry.LocalMachine : Registry.CurrentUser;
                using (RegistryKey k = hive.OpenSubKey(subKey))
                {
                    if (k == null) return false;
                    object raw = k.GetValue(valueName);
                    if (raw == null) return false;
                    byte[] data = raw as byte[];
                    if (data == null || data.Length == 0) return false;
                    // 0x02 = 启用，其余（0x03 / 0x06 …）视为禁用
                    return data[0] != 2 && data[0] != 0 && data[0] != 1;
                }
            }
            catch (Exception) { return false; }
        }

        private static void ReadStartupFolders(List<StartupItem> items)
        {
            string[] folders = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup")
            };
            for (int f = 0; f < folders.Length; f++)
            {
                if (!Directory.Exists(folders[f])) continue;
                string[] files;
                try { files = Directory.GetFileSystemEntries(folders[f]); }
                catch (Exception) { continue; }
                for (int i = 0; i < files.Length; i++)
                {
                    string n = Path.GetFileName(files[i]);
                    if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    StartupItem it = new StartupItem();
                    it.Name = Path.GetFileNameWithoutExtension(n);
                    it.Command = files[i];
                    it.IsFile = true;
                    it.FilePath = files[i];
                    it.Machine = f == 1;
                    it.Group = f == 0 ? "user" : "sys";
                    it.Source = f == 0 ? Lang.T("用户启动文件夹") : Lang.T("公共启动文件夹");
                    it.ApprovalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
                    it.ApprovalName = n;
                    it.Enabled = !ApprovalDisabled(it.Machine, it.ApprovalPath, it.ApprovalName);
                    items.Add(it);
                }
            }
        }

        /// <summary>Active Setup：IE/Edge/Office 等组件在登录时执行一次的初始化命令。</summary>
        private static void ReadActiveSetup(List<StartupItem> items)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Active Setup\Installed Components"))
                {
                    if (root == null) return;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length && i < 120; i++)
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(subs[i]))
                            {
                                if (k == null) continue;
                                object stub = k.GetValue("StubPath");
                                if (stub == null) continue;
                                string cmd = stub.ToString().Trim();
                                if (cmd.Length == 0) continue;
                                object disp = k.GetValue(null);
                                StartupItem it = new StartupItem();
                                it.Name = disp == null ? subs[i] : disp.ToString();
                                it.Command = cmd;
                                it.Group = "sys";
                                it.Machine = true;
                                it.HiveName = "HKLM";
                                it.KeyPath = @"Software\Microsoft\Active Setup\Installed Components\" + subs[i];
                                it.ValueName = "StubPath";
                                it.Source = "Active Setup";
                                it.Enabled = true;
                                items.Add(it);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>计划任务中带“登录/开机”触发的部分。</summary>
        private static void ReadTasks(List<StartupItem> items)
        {
            string root = @"C:\Windows\System32\Tasks";
            if (!Directory.Exists(root)) return;
            WalkTasks(root, "", items, 0);
        }

        private static void WalkTasks(string dir, string rel, List<StartupItem> items, int depth)
        {
            if (depth > 4 || items.Count > 400) return;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { files = new string[0]; }
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string xml;
                try
                {
                    FileInfo fi = new FileInfo(files[i]);
                    if (fi.Length > 512 * 1024) continue;
                    xml = File.ReadAllText(files[i]);
                }
                catch (Exception) { continue; }
                bool logon = xml.IndexOf("<LogonTrigger", StringComparison.OrdinalIgnoreCase) >= 0;
                bool boot = xml.IndexOf("<BootTrigger", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!logon && !boot) continue;
                string taskPath = rel.Length == 0 ? fileName : rel + "\\" + fileName;
                StartupItem it = new StartupItem();
                it.Name = fileName;
                it.IsTask = true;
                it.TaskPath = taskPath;
                it.Group = "task";
                it.Source = logon ? Lang.T("登录时触发") : Lang.T("开机时触发");
                it.Machine = true;
                it.Command = TaskCommand(xml);
                it.Enabled = TaskEnabled(taskPath, xml);
                items.Add(it);
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < subs.Length; i++)
            {
                string name = Path.GetFileName(subs[i]);
                WalkTasks(subs[i], rel.Length == 0 ? name : rel + "\\" + name, items, depth + 1);
            }
        }

        private static string TaskCommand(string xml)
        {
            int a = xml.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase);
            if (a < 0) return Lang.T("（任务定义见任务计划程序）");
            a += "<Command>".Length;
            int b = xml.IndexOf("</Command>", a, StringComparison.OrdinalIgnoreCase);
            if (b <= a) return Lang.T("（任务定义见任务计划程序）");
            string cmd = xml.Substring(a, b - a).Trim();
            int argA = xml.IndexOf("<Arguments>", StringComparison.OrdinalIgnoreCase);
            if (argA > 0)
            {
                argA += "<Arguments>".Length;
                int argB = xml.IndexOf("</Arguments>", argA, StringComparison.OrdinalIgnoreCase);
                if (argB > argA) cmd += " " + xml.Substring(argA, argB - argA).Trim();
            }
            return cmd;
        }

        private static bool TaskEnabled(string taskPath, string xml)
        {
            // 任务定义里带 Enabled 标记时直接读取，避免为每个任务启动一次 schtasks
            int a = xml.IndexOf("<Enabled>", StringComparison.OrdinalIgnoreCase);
            if (a >= 0)
            {
                string tail = xml.Substring(a + 9, Math.Min(8, xml.Length - a - 9)).ToLowerInvariant();
                if (tail.StartsWith("false")) return false;
                if (tail.StartsWith("true")) return true;
            }
            string output;
            RunTool("schtasks.exe", "/Query /TN \"" + taskPath + "\" /FO LIST", out output);
            if (output == null) return true;
            if (output.IndexOf(Lang.T("已禁用"), StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (output.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        /// <summary>自动启动的 Win32 服务（只读展示，避免误停系统服务）。</summary>
        private static void ReadServices(List<StartupItem> items)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (root == null) return;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length && items.Count < 600; i++)
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(subs[i]))
                            {
                                if (k == null) continue;
                                object startObj = k.GetValue("Start");
                                object typeObj = k.GetValue("Type");
                                if (startObj == null || typeObj == null) continue;
                                int start = Convert.ToInt32(startObj);
                                int type = Convert.ToInt32(typeObj);
                                if (start != 2) continue;               // 仅自动启动
                                if (type != 0x10 && type != 0x20) continue; // 仅 Win32 服务，排除驱动
                                object disp = k.GetValue("DisplayName");
                                string label = disp == null ? subs[i] : disp.ToString();
                                label = ResolveIndirect(label);
                                if (label.Length == 0 || label.StartsWith("@")) label = subs[i];
                                StartupItem it = new StartupItem();
                                it.Name = label;
                                it.Command = subs[i];
                                object imgObj = k.GetValue("ImagePath");
                                string img = imgObj == null ? "" : ResolveIndirect(imgObj.ToString());
                                if (img.Length == 0) img = subs[i];
                                // 映像在 System32（含 svchost 托管）算系统关键服务，其余算应用软件服务
                                bool systemOwned = img.IndexOf("system32", StringComparison.OrdinalIgnoreCase) >= 0
                                                || img.IndexOf("%SystemRoot%", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (systemOwned && img.IndexOf("svchost", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // 托管服务看 ServiceDll 落在哪里，别把第三方 DLL 算成系统服务
                                    string dll = ServiceDll(k);
                                    if (dll.Length > 0) systemOwned = dll.IndexOf("system32", StringComparison.OrdinalIgnoreCase) >= 0
                                                                  || dll.IndexOf("%SystemRoot%", StringComparison.OrdinalIgnoreCase) >= 0;
                                }
                                it.Group = systemOwned ? "syssvc" : "appsvc";
                                it.IsService = true;
                                it.Machine = true;
                                it.Source = systemOwned ? Lang.T("系统关键服务（自动启动）") : Lang.T("应用软件服务（自动启动）");
                                it.Enabled = true;
                                it.Detail = Lang.T("服务名 ") + subs[i] + " · " + img;
                                items.Add(it);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static string ServiceDll(RegistryKey svcKey)
        {
            try
            {
                using (RegistryKey p = svcKey.OpenSubKey("Parameters"))
                {
                    if (p == null) return "";
                    object v = p.GetValue("ServiceDll");
                    return v == null ? "" : v.ToString();
                }
            }
            catch (Exception) { return ""; }
        }

        /// <summary>随开机/右键/浏览器自动加载的插件：BHO、图标叠加、右键扩展（只读展示）。</summary>
        private static void ReadPlugins(List<StartupItem> items)
        {
            // 浏览器辅助对象（BHO）
            AddShellKey(items, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", Lang.T("浏览器插件（BHO）"), true);
            AddShellKey(items, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", Lang.T("浏览器插件（BHO）"), false);
            // 资源管理器图标叠加
            AddShellKey(items, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers", Lang.T("图标叠加"), true);
            // 右键菜单扩展
            AddShellKey(items, Registry.ClassesRoot, @"*\shellex\ContextMenuHandlers", Lang.T("右键菜单扩展"), true);
            AddShellKey(items, Registry.ClassesRoot, @"Directory\shellex\ContextMenuHandlers", Lang.T("文件夹右键扩展"), true);
        }

        private static void AddShellKey(List<StartupItem> items, RegistryKey hive, string path, string kind, bool machine)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(path))
                {
                    if (root == null) return;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length && items.Count < 700; i++)
                    {
                        string clsid = subs[i].Trim();
                        string label = ClsidName(clsid);
                        if (label.Length == 0) label = clsid;
                        if (label.Length == 0) continue;
                        StartupItem it = new StartupItem();
                        it.Name = label;
                        it.Command = clsid;
                        it.Group = "plugin";
                        it.IsService = true;            // 复用“只读”标记
                        it.Machine = machine;
                        it.Source = kind;
                        it.Enabled = true;
                        it.Detail = kind + " · " + ClsidPath(clsid);
                        items.Add(it);
                    }
                }
            }
            catch (Exception) { }
        }

        private static string ClsidName(string clsid)
        {
            if (!clsid.StartsWith("{")) return "";
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid))
                {
                    if (k == null) return "";
                    object v = k.GetValue(null);
                    return v == null ? "" : v.ToString();
                }
            }
            catch (Exception) { return ""; }
        }

        private static string ClsidPath(string clsid)
        {
            if (!clsid.StartsWith("{")) return "";
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid + "\\InprocServer32"))
                {
                    if (k == null) return "";
                    object v = k.GetValue(null);
                    return v == null ? "" : v.ToString();
                }
            }
            catch (Exception) { return ""; }
        }

        /// <summary>把本程序此前禁用、仍留在备份里的项也列出来，便于一键还原。</summary>
        private static void ReadDisabled(List<StartupItem> items)
        {
            try
            {
                if (!File.Exists(BackupFile)) return;
                string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 7) continue;
                    bool dup = false;
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (items[k].ValueName == p[4] && items[k].KeyPath == p[3]) { dup = true; break; }
                    }
                    if (dup) continue;
                    StartupItem it = new StartupItem();
                    it.Name = p[1];
                    it.Command = Encoding.UTF8.GetString(Convert.FromBase64String(p[6]));
                    it.IsFile = p[0] == "file";
                    it.Machine = p[2] == "HKLM";
                    it.HiveName = p[2];
                    it.KeyPath = p[3];
                    it.ValueName = p[4];
                    it.FilePath = p[5];
                    it.Enabled = false;
                    it.Group = it.IsFile ? (it.Machine ? "sys" : "user") : "user";
                    it.Source = it.IsFile ? Lang.T("启动文件夹（已备份）") : (p[2] + " " + ShortKey(p[3]) + Lang.T("（已备份）"));
                    items.Add(it);
                }
            }
            catch (Exception) { }
        }

        public static List<StartupItem> ListDisabled()
        {
            List<StartupItem> items = new List<StartupItem>();
            try
            {
                if (!File.Exists(BackupFile)) return items;
                string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 7) continue;
                    StartupItem it = new StartupItem();
                    it.Name = p[1];
                    it.Command = Encoding.UTF8.GetString(Convert.FromBase64String(p[6]));
                    it.Source = p[0] == "file" ? Lang.T("启动文件夹（已备份）") : (p[2] + Lang.T(" Run（已备份）"));
                    it.Enabled = false;
                    it.IsFile = p[0] == "file";
                    it.Machine = p[2] == "HKLM";
                    it.HiveName = p[2];
                    it.KeyPath = p[3];
                    it.ValueName = p[4];
                    it.FilePath = p[5];
                    items.Add(it);
                }
            }
            catch (Exception) { }
            return items;
        }

        public static bool Disable(StartupItem it, out string message)
        {
            message = "";
            if (it.IsService)
            {
                message = Lang.T("系统服务不在此处停用，请在“服务”管理器或系统配置中操作");
                return false;
            }
            if (it.IsTask)
            {
                string output;
                bool ok = RunTool("schtasks.exe", "/Change /TN \"" + it.TaskPath + "\" /Disable", out output);
                if (ok)
                {
                    OpLog.Write("startup", Lang.T("禁用任务 ") + it.Name, 0);
                    message = Lang.T("计划任务已禁用，可在本页一键启用");
                    return true;
                }
                message = Lang.T("任务禁用失败：") + (output == null ? Lang.T("需要管理员权限") : output.Trim());
                return false;
            }
            if (it.IsFile)
            {
                try
                {
                    string dest = Path.Combine(BackupDir, Path.GetFileName(it.FilePath));
                    if (File.Exists(dest)) dest = dest + "." + DateTime.Now.Ticks;
                    File.Move(it.FilePath, dest);
                    AppendBackup("file", it.HiveName, "", "", dest, it.FilePath, Encoding.UTF8.GetBytes(it.FilePath));
                    OpLog.Write("startup", Lang.T("禁用 ") + it.Name, 0);
                    message = Lang.T("已从启动文件夹移除（文件已备份）");
                    return true;
                }
                catch (Exception ex) { message = Lang.T("操作失败：") + ex.Message; return false; }
            }
            try
            {
                RegistryKey hive = it.HiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using (RegistryKey k = hive.OpenSubKey(it.KeyPath, true))
                {
                    if (k == null) { message = Lang.T("无法打开注册表项"); return false; }
                    object val = k.GetValue(it.ValueName);
                    RegistryValueKind kind = k.GetValueKind(it.ValueName);
                    byte[] data;
                    if (val is byte[]) data = (byte[])val;
                    else if (val is string[]) data = Encoding.UTF8.GetBytes(string.Join("\n", (string[])val));
                    else data = Encoding.UTF8.GetBytes(val == null ? "" : val.ToString());
                    AppendBackup("reg", it.HiveName, it.KeyPath, it.ValueName, kind.ToString(), "", data);
                    k.DeleteValue(it.ValueName, false);
                    OpLog.Write("startup", Lang.T("禁用 ") + it.Name, 0);
                    message = Lang.T("已禁用（已备份，可随时还原）");
                    return true;
                }
            }
            catch (Exception ex) { message = Lang.T("操作失败：") + ex.Message; return false; }
        }

        public static bool Enable(StartupItem it, out string message)
        {
            message = "";
            if (it.IsService)
            {
                message = Lang.T("系统服务不在此处启用，请在“服务”管理器操作");
                return false;
            }
            if (it.IsTask)
            {
                string output;
                bool ok = RunTool("schtasks.exe", "/Change /TN \"" + it.TaskPath + "\" /Enable", out output);
                if (ok)
                {
                    OpLog.Write("startup", Lang.T("启用任务 ") + it.Name, 0);
                    message = Lang.T("计划任务已重新启用");
                    return true;
                }
                message = Lang.T("任务启用失败：") + (output == null ? Lang.T("需要管理员权限") : output.Trim());
                return false;
            }
            try
            {
                if (it.IsFile && File.Exists(it.FilePath)) { message = Lang.T("已存在"); return true; }
                if (it.IsFile)
                {
                    string src = Path.Combine(BackupDir, Path.GetFileName(it.FilePath));
                    if (!File.Exists(src)) { message = Lang.T("备份文件不存在"); return false; }
                    File.Copy(src, it.FilePath, true);
                    RemoveBackup("file", it.FilePath, "");
                    OpLog.Write("startup", Lang.T("启用 ") + it.Name, 0);
                    message = Lang.T("已恢复到启动文件夹");
                    return true;
                }
                if (it.ApprovalPath.Length > 0 && ApprovalDisabled(it.Machine, it.ApprovalPath, it.ApprovalName))
                {
                    RegistryKey hive2 = it.Machine ? Registry.LocalMachine : Registry.CurrentUser;
                    using (RegistryKey ka = hive2.OpenSubKey(it.ApprovalPath, true))
                    {
                        if (ka != null) ka.DeleteValue(it.ApprovalName, false);
                    }
                    OpLog.Write("startup", Lang.T("启用 ") + it.Name, 0);
                    message = Lang.T("已解除系统禁用标记");
                    return true;
                }
                RegistryKey hive = it.HiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using (RegistryKey k = hive.OpenSubKey(it.KeyPath, true))
                {
                    if (k == null) { message = Lang.T("无法打开注册表项"); return false; }
                    byte[] data = ReadBackupData(it.ValueName, it.FilePath, it.KeyPath);
                    if (data == null) { message = Lang.T("找不到备份数据"); return false; }
                    RegistryValueKind kind = ReadBackupKind(it.ValueName, it.KeyPath);
                    if (kind == RegistryValueKind.Binary) k.SetValue(it.ValueName, data, kind);
                    else if (kind == RegistryValueKind.MultiString) k.SetValue(it.ValueName, Encoding.UTF8.GetString(data).Split('\n'), kind);
                    else if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
                    {
                        uint v = 0; uint.TryParse(Encoding.UTF8.GetString(data), out v);
                        k.SetValue(it.ValueName, (int)v, kind);
                    }
                    else k.SetValue(it.ValueName, Encoding.UTF8.GetString(data), kind);
                    RemoveBackup("reg", it.ValueName, it.KeyPath);
                    OpLog.Write("startup", Lang.T("启用 ") + it.Name, 0);
                    message = Lang.T("已重新启用");
                    return true;
                }
            }
            catch (Exception ex) { message = Lang.T("操作失败：") + ex.Message; return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string name, IntPtr reserved, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int LoadString(IntPtr module, uint id, StringBuilder buffer, int size);

        /// <summary>把注册表里的间接字符串（@%SystemRoot%\system32\x.dll,-100）解析成真实名称。</summary>
        private static string ResolveIndirect(string raw)
        {
            if (raw == null || raw.Length == 0) return "";
            if (!raw.StartsWith("@")) return raw;
            try
            {
                int comma = raw.LastIndexOf(',');
                if (comma <= 1) return raw;
                string file = Environment.ExpandEnvironmentVariables(raw.Substring(1, comma - 1).Trim());
                int id;
                if (!int.TryParse(raw.Substring(comma + 1).Trim(), out id)) return raw;
                if (id < 0) id = -id;
                IntPtr h = LoadLibraryEx(file, IntPtr.Zero, 0x00000002); // LOAD_LIBRARY_AS_DATAFILE
                if (h == IntPtr.Zero) return raw;
                try
                {
                    StringBuilder sb = new StringBuilder(512);
                    int n = LoadString(h, (uint)id, sb, sb.Capacity);
                    if (n > 0) return sb.ToString();
                }
                finally { FreeLibrary(h); }
                return raw;
            }
            catch (Exception) { return raw; }
        }

        /// <summary>调用系统工具并取输出（schtasks）。</summary>
        private static bool RunTool(string exe, string args, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(exe, args);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                si.StandardOutputEncoding = Encoding.GetEncoding(936);
                using (Process p = Process.Start(si))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(4000))
                    {
                        try { p.Kill(); }
                        catch (Exception) { }
                        output = Lang.T("命令执行超时");
                        return false;
                    }
                    output = (o + e).Trim();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }

        private static void AppendBackup(string type, string hive, string keyPath, string valueName, string extra, string filePath, byte[] data)
        {
            try
            {
                string line = type + "\t" + hive + "\t" + keyPath + "\t" + valueName + "\t" + extra + "\t" + filePath + "\t" + Convert.ToBase64String(data) + "\r\n";
                File.AppendAllText(BackupFile, line, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        private static byte[] ReadBackupData(string valueName, string filePath, string keyPath)
        {
            string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] p = lines[i].Split('\t');
                if (p.Length < 7) continue;
                if (p[3] == valueName && p[2] == keyPath) return Convert.FromBase64String(p[6]);
            }
            return null;
        }

        private static RegistryValueKind ReadBackupKind(string valueName, string keyPath)
        {
            try
            {
                string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 7) continue;
                    if (p[3] == valueName && p[2] == keyPath)
                    {
                        return (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), p[4]);
                    }
                }
            }
            catch (Exception) { }
            return RegistryValueKind.String;
        }

        private static void RemoveBackup(string type, string key, string keyPath)
        {
            try
            {
                if (!File.Exists(BackupFile)) return;
                List<string> keep = new List<string>();
                string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 7) continue;
                    if (p[0] == type && p[3] == key && p[2] == keyPath) continue;
                    keep.Add(lines[i]);
                }
                File.WriteAllLines(BackupFile, keep.ToArray(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
