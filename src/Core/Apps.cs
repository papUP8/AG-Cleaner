using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AGCleaner.Core
{
    public class App
    {
        public string Name = "";
        public string Version = "";
        public string Publisher = "";
        public string Location = "";
        public string Uninstall = "";
        public string Quiet = "";
        public string IconPath = "";
        public long Bytes = 0;
        public string Hive = "HKLM";
        public string KeyPath = "";
        public string KeyName = "";
        public bool Machine = true;

        public string SizeText { get { return Bytes > 0 ? Byt.Format(Bytes) : "—"; } }
    }

    public static class Apps
    {
        private static readonly string[] UninstallRoots = new string[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        public static List<App> List(Action<string> report)
        {
            List<App> apps = new List<App>();
            if (report != null) report(Lang.T("读取已安装程序..."));
            foreach (string root in UninstallRoots)
            {
                ReadHive(Registry.LocalMachine, root, true, apps);
            }
            ReadHive(Registry.CurrentUser, UninstallRoots[0], false, apps);

            // 去重（同名同版本）
            List<App> unique = new List<App>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int i = 0; i < apps.Count; i++)
            {
                string k = apps[i].Name.ToLowerInvariant() + "|" + apps[i].Version;
                if (seen.ContainsKey(k)) continue;
                seen[k] = true;
                unique.Add(apps[i]);
            }

            if (report != null) report(Lang.T("统计安装体积（约 8 秒）..."));
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i].Bytes > 0) continue;
                if (sw.Elapsed.TotalSeconds > 9) break;
                string loc = unique[i].Location;
                if (loc.Length == 0 || !Directory.Exists(loc)) continue;
                if (loc.Length < 10) continue;
                unique[i].Bytes = FileUtil.SizeOf(loc);
            }
            unique.Sort(delegate (App a, App b) { return b.Bytes.CompareTo(a.Bytes); });
            return unique;
        }

        private static void ReadHive(RegistryKey hive, string rootPath, bool machine, List<App> apps)
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
                                if (ToInt(k.GetValue("SystemComponent")) == 1) continue;
                                if (Str(k.GetValue("ParentKeyName")).Length > 0) continue;
                                string release = Str(k.GetValue("ReleaseType"));
                                if (release.ToLowerInvariant().Contains("update")) continue;

                                App a = new App();
                                a.Name = display;
                                a.Version = Str(k.GetValue("DisplayVersion"));
                                a.Publisher = Str(k.GetValue("Publisher"));
                                a.Location = Str(k.GetValue("InstallLocation")).Trim('"');
                                a.Uninstall = Str(k.GetValue("UninstallString"));
                                a.Quiet = Str(k.GetValue("QuietUninstallString"));
                                a.Machine = machine;
                                a.KeyPath = rootPath + "\\" + names[i];
                                a.KeyName = names[i];
                                a.Hive = machine ? "HKLM" : "HKCU";
                                object size = k.GetValue("EstimatedSize");
                                long kb = 0;
                                if (size != null) { long.TryParse(size.ToString(), out kb); }
                                a.Bytes = kb * 1024L;
                                string icon = Str(k.GetValue("DisplayIcon"));
                                a.IconPath = CleanIconPath(icon);
                                if (a.Uninstall.Length == 0) continue;
                                apps.Add(a);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static string CleanIconPath(string icon)
        {
            if (icon.Length == 0) return "";
            string p = icon.Trim();
            int comma = p.LastIndexOf(',');
            if (comma > 0) p = p.Substring(0, comma);
            return p.Trim('"');
        }

        private static string Str(object o)
        {
            if (o == null) return "";
            return o.ToString();
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            int v = 0;
            int.TryParse(o.ToString(), out v);
            return v;
        }

        /// <summary>调用应用自带卸载器，返回是否已启动并结束。</summary>
        public static bool RunUninstaller(App app, bool silent, out string message)
        {
            message = "";
            string cmd = silent && app.Quiet.Length > 0 ? app.Quiet : app.Uninstall;
            if (cmd.Length == 0) { message = Lang.T("找不到卸载命令"); return false; }

            string lower = cmd.ToLowerInvariant();
            if ((lower.Contains("msiexec") || lower.Contains("msi")) && lower.Contains("/i"))
            {
                int idx = lower.IndexOf("/i");
                cmd = cmd.Substring(0, idx) + "/X" + cmd.Substring(idx + 2);
                if (silent)
                {
                    cmd = cmd.Replace("/qn", "").Replace("/QN", "");
                    if (!cmd.ToLowerInvariant().Contains("/q")) cmd += " /qn";
                }
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c " + cmd;
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Normal;
                if (app.Machine && !Native.IsAdmin())
                {
                    psi.Verb = "runas";
                }
                Process p = Process.Start(psi);
                if (p == null) { message = Lang.T("无法启动卸载器"); return false; }
                p.WaitForExit();
                message = Lang.T("卸载器已结束 (exit ") + p.ExitCode + ")";
                OpLog.Write("uninstall", app.Name, 0);
                return true;
            }
            catch (Exception ex)
            {
                message = Lang.T("卸载失败：") + ex.Message;
                return false;
            }
        }

        private static string Sanitize(string name)
        {
            string s = Regex.Replace(name, @"[\\/:*?""<>|]", "");
            s = s.Trim();
            return s;
        }

        /// <summary>卸载后扫描可能的残留目录。</summary>
        public static List<CleanItem> FindLeftovers(App app, Action<string> report)
        {
            List<CleanItem> items = new List<CleanItem>();
            List<string> candidates = new List<string>();
            if (app.Location.Length > 0 && Directory.Exists(app.Location)) candidates.Add(app.Location);

            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ra = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            string name = Sanitize(app.Name);
            string pub = Sanitize(app.Publisher);
            if (name.Length > 1)
            {
                candidates.Add(Path.Combine(la, name));
                candidates.Add(Path.Combine(ra, name));
                candidates.Add(Path.Combine(la, "Programs", name));
                candidates.Add(Path.Combine(pd, name));
            }
            if (pub.Length > 1 && pub.Length < 20)
            {
                candidates.Add(Path.Combine(la, pub));
                candidates.Add(Path.Combine(ra, pub));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (!Directory.Exists(c)) continue;
                if (!Guard.CanDelete(c, false)) continue;
                long size = FileUtil.SizeOf(c);
                if (size < 1024) continue;
                bool dup = false;
                for (int j = 0; j < items.Count; j++) if (items[j].Path == c) dup = true;
                if (dup) continue;
                CleanItem it = new CleanItem();
                it.Name = app.Name + Lang.T(" 残留");
                it.Path = c;
                it.Kind = CleanKind.DirItself;
                it.Bytes = size;
                it.Recycle = true;
                it.Exists = true;
                items.Add(it);
            }
            if (report != null) report(Lang.T("残留扫描完成：") + items.Count + Lang.T(" 项"));
            return items;
        }
    }
}
