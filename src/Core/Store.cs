using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGCleaner.Core
{
    /// <summary>本地配置与操作日志。放在 %USERPROFILE%\.config\agcleaner 下。</summary>
    public static class Store
    {
        private static bool _migrated;

        public static string RootDir
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dir = @"D:\doubao00\AGCleaner\config";
                if (!_migrated) { _migrated = true; MigrateLegacy(home, dir); }
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>从旧品牌目录 .config\ccdesk 迁移配置与日志，老用户不用重新设置。</summary>
        private static void MigrateLegacy(string home, string dir)
        {
            try
            {
                string old = Path.Combine(home, ".config", "agcleaner");
                if (!Directory.Exists(old)) old = Path.Combine(home, ".config", "ccdesk");
                if (!Directory.Exists(old)) return;
                if (File.Exists(Path.Combine(dir, "settings.ini"))) return;
                Directory.CreateDirectory(dir);
                foreach (string name in new string[] { "settings.ini", "whitelist.txt", "operations.log", "crash.log" })
                {
                    string from = Path.Combine(old, name);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(dir, name), true);
                }
                string oldBackup = Path.Combine(old, "backup");
                if (Directory.Exists(oldBackup))
                {
                    string newBackup = Path.Combine(dir, "backup");
                    Directory.CreateDirectory(newBackup);
                    foreach (string f in Directory.GetFiles(oldBackup))
                    {
                        File.Copy(f, Path.Combine(newBackup, Path.GetFileName(f)), true);
                    }
                }
            }
            catch (Exception) { }
        }

        public static string SettingsFile { get { return Path.Combine(RootDir, "settings.ini"); } }
        public static string WhitelistFile { get { return Path.Combine(RootDir, "whitelist.txt"); } }
        public static string OpLogFile { get { return Path.Combine(RootDir, "operations.log"); } }

        public static string ReadLine(string file)
        {
            try
            {
                if (File.Exists(file)) return File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception) { }
            return "";
        }

        public static void WriteAll(string file, string content)
        {
            try { File.WriteAllText(file, content, Encoding.UTF8); }
            catch (Exception) { }
        }
    }

    public class Settings
    {
        // 清理工具的目的就是释放空间：把垃圾挪进回收站等于没清，默认改为永久删除
        public bool UseRecycleBin = false;
        public bool BinModeMigrated = false;
        public int TempDays = 0;
        public bool CleanGpuShaders = false;
        public bool CleanProjectArtifacts = false;
        public bool DeleteOldInstallers = true;
        public string ThemeMode = "dark";
        public string Language = "zh";
        public string ScanDrives = "";   // 逗号分隔的盘符（如 "C,D"）；空表示全部固定盘
        public List<string> ProjectDirs = new List<string>();
        public bool ConfirmBeforeDelete = true;
        public bool TempMigrated = false;

        public static Settings Load()
        {
            Settings s = new Settings();
            string text = Store.ReadLine(Store.SettingsFile);
            string[] lines = text.Split(new char[] { '\n' });
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k == "recycle") s.UseRecycleBin = v == "1";
                else if (k == "binmig") s.BinModeMigrated = v == "1";
                else if (k == "tempdays") { try { s.TempDays = int.Parse(v); } catch (Exception) { } }
                else if (k == "gpu") s.CleanGpuShaders = v == "1";
                else if (k == "projects") s.CleanProjectArtifacts = v == "1";
                else if (k == "installers") s.DeleteOldInstallers = v == "1";
                else if (k == "theme") s.ThemeMode = v;
                else if (k == "lang") s.Language = v;
                else if (k == "scandrives") s.ScanDrives = v;
                else if (k == "tempmig") s.TempMigrated = v == "1";
                else if (k == "dir") { if (v.Length > 0) s.ProjectDirs.Add(v); }
            }
            // 老版本默认把删除项移入回收站，结果垃圾只是换了位置、空间一点没释放；
            // 迁移一次改为永久删除，之后以用户在设置页的选择为准
            if (!s.BinModeMigrated)
            {
                s.UseRecycleBin = false;
                s.BinModeMigrated = true;
                s.Save();
            }
            // 早期默认是“只清 7 天前的临时文件”，会漏掉大量仍可安全删除的临时文件；
            // 只迁移一次，之后以用户在设置页选择的天数为准
            if (!s.TempMigrated)
            {
                if (s.TempDays == 7) s.TempDays = 0;
                s.TempMigrated = true;
                s.Save();
            }
            if (s.ProjectDirs.Count == 0)
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (docs.Length > 0) s.ProjectDirs.Add(docs);
            }
            return s;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.T("# AG Cleaner 配置"));
            sb.AppendLine("recycle=" + (UseRecycleBin ? "1" : "0"));
            sb.AppendLine("binmig=1");
            sb.AppendLine("tempdays=" + TempDays);
            sb.AppendLine("tempmig=1");
            sb.AppendLine("gpu=" + (CleanGpuShaders ? "1" : "0"));
            sb.AppendLine("projects=" + (CleanProjectArtifacts ? "1" : "0"));
            sb.AppendLine("installers=" + (DeleteOldInstallers ? "1" : "0"));
            sb.AppendLine("theme=" + ThemeMode);
            sb.AppendLine("lang=" + Language);
            sb.AppendLine("scandrives=" + ScanDrives);
            foreach (string d in ProjectDirs) sb.AppendLine("dir=" + d);
            Store.WriteAll(Store.SettingsFile, sb.ToString());
        }
    }

    /// <summary>路径安全护栏：受保护目录、敏感路径片段、用户白名单。</summary>
    public static class Guard
    {
        public static readonly string[] DefaultWhitelist = new string[]
        {
            @"%LOCALAPPDATA%\Microsoft\Windows\Fonts",
            @"%USERPROFILE%\.vscode\extensions",
            @"%USERPROFILE%\.nuget",
            @"%USERPROFILE%\.cargo",
            @"%USERPROFILE%\.rustup",
            @"%USERPROFILE%\.m2\repository",
            @"%USERPROFILE%\.ollama\models",
            @"%LOCALAPPDATA%\JetBrains"
        };

        private static readonly string[] ProtectedRoots = new string[]
        {
            "C:\\Windows",
            "C:\\Program Files",
            "C:\\Program Files (x86)",
            "C:\\Program Files\\Windows Defender",
            "C:\\Program Files (x86)\\Windows Defender",
            "C:\\ProgramData\\Microsoft\\Windows Defender"
        };

        /// <summary>系统目录里明确可清理的子路径（只放开这些，别处一律不动）。</summary>
        private static readonly string[] TrustedForClean = new string[]
        {
            "C:\\Windows\\Temp",
            "C:\\Windows\\Logs",
            "C:\\Windows\\System32\\WDI",
            "C:\\Windows\\System32\\WDI\\LogFiles",
            "C:\\Windows\\WinSxS\\Temp",
            "C:\\Windows\\SoftwareDistribution\\Download",
            "C:\\Windows\\Prefetch",
            "C:\\Windows\\Minidump",
            "C:\\Windows\\Panther",
            "C:\\Windows\\System32\\winevt\\Logs",
            "C:\\Windows\\System32\\LogFiles",
            "C:\\Windows\\Installer",
            "C:\\Windows\\INF",
            "C:\\Windows\\ServiceProfiles\\NetworkService\\AppData\\Local\\Temp",
            "C:\\Windows\\ServiceProfiles\\LocalService\\AppData\\Local\\FontCache",
            "C:\\Windows\\ServiceProfiles\\LocalService\\AppData\\Local\\Temp",
            "C:\\Windows\\LiveKernelReports",
            "C:\\Windows\\System32\\DriverStore\\Temp",
            "C:\\Windows\\SoftwareDistribution\\PostRebootEventCache",
            "C:\\Windows\\SoftwareDistribution\\DataStore\\Logs",
            "C:\\ProgramData\\Microsoft\\Search\\Data",
            "C:\\ProgramData\\Microsoft\\Windows\\WER",
            "C:\\ProgramData\\Microsoft\\Windows Defender\\Scans\\History\\Results",
            "C:\\ProgramData\\Microsoft\\Network\\Downloader",
            "C:\\ProgramData\\Intel\\Package Cache"
        };

        private static readonly string[] SensitiveSegments = new string[]
        {
            "Microsoft\\Credentials",
            "Microsoft\\Protect",
            "Microsoft\\Crypto",
            "Microsoft\\Vault",
            "Microsoft\\SystemCertificates",
            "Microsoft\\Windows\\Start Menu"
        };

        private static readonly string[] CleanSensitiveSegments = new string[]
        {
            ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker",
            "Login Data", "Cookies", "Web Data", "Local Storage", "Session Storage",
            "IndexedDB", "Sessions", "Local Extension Settings", "Sync Data",
            "Service Worker\\CacheStorage", "Adobe\\Common\\Team Projects Cache",
            "pypoetry\\Cache\\virtualenvs", ".claude\\projects", ".claude\\file-history",
            ".codex\\sessions", ".codex\\archived_sessions"
        };

        private static List<string> _whitelist;

        public static string Expand(string p)
        {
            if (p == null) return "";
            return Environment.ExpandEnvironmentVariables(p);
        }

        public static string Norm(string path)
        {
            if (path == null) return "";
            string p = path.Trim().Trim('"');
            try { p = Path.GetFullPath(Expand(p)); }
            catch (Exception) { }
            p = Native.LongPath(p);
            p = p.TrimEnd('\\', '/');
            return p.ToLowerInvariant();
        }

        public static List<string> WhiteList()
        {
            if (_whitelist != null) return _whitelist;
            List<string> list = new List<string>();
            string saved = Store.ReadLine(Store.WhitelistFile);
            string[] lines = saved.Split(new char[] { '\n' });
            foreach (string l in lines)
            {
                string t = l.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) list.Add(t);
            }
            if (list.Count == 0)
            {
                foreach (string d in DefaultWhitelist) list.Add(d);
                SaveWhiteList(list);
            }
            else
            {
                // 迁移：早期默认白名单里有缩略图与最近文档目录，会让对应缓存永远清不掉，加载时移除
                bool changed = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    string norm = Norm(list[i]);
                    if (norm.EndsWith("\\microsoft\\windows\\explorer") || norm.EndsWith("\\microsoft\\windows\\recent"))
                    {
                        list.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed) SaveWhiteList(list);
            }
            _whitelist = list;
            return list;
        }

        public static void SaveWhiteList(List<string> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.T("# AG Cleaner 白名单：每行一个路径，命中则不清理（支持 %ENV% 变量）"));
            foreach (string s in list) sb.AppendLine(s);
            Store.WriteAll(Store.WhitelistFile, sb.ToString());
            _whitelist = null;
        }

        public static void AddWhitelist(string path)
        {
            List<string> list = new List<string>(WhiteList());
            if (!list.Contains(path)) list.Add(path);
            SaveWhiteList(list);
        }

        public static void RemoveWhitelist(string path)
        {
            List<string> list = new List<string>(WhiteList());
            list.Remove(path);
            SaveWhiteList(list);
        }

        /// <summary>是否属于绝对不能碰的系统位置。</summary>
        public static bool IsProtected(string path)
        {
            string p = Norm(path);
            if (p.Length == 0) return true;
            string profile = Norm(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (p == profile) return true;
            if (IsTrustedForClean(p))
            {
                // 放开的是具体子路径，其下再有敏感片段仍然拒绝
                foreach (string seg in SensitiveSegments)
                {
                    if (p.Contains("\\" + seg.ToLowerInvariant() + "\\") || p.EndsWith("\\" + seg.ToLowerInvariant())) return true;
                }
                return false;
            }
            foreach (string root in ProtectedRoots)
            {
                string r = Norm(root);
                if (p == r || p.StartsWith(r + "\\")) return true;
            }
            foreach (string seg in SensitiveSegments)
            {
                if (p.Contains("\\" + seg.ToLowerInvariant() + "\\") || p.EndsWith("\\" + seg.ToLowerInvariant())) return true;
            }
            return false;
        }

        /// <summary>是否为系统目录里明确允许清理的子路径。</summary>
        public static bool IsTrustedForClean(string path)
        {
            string p = Norm(path);
            foreach (string t in TrustedForClean)
            {
                string r = Norm(t);
                if (p == r || p.StartsWith(r + "\\")) return true;
            }
            return false;
        }

        /// <summary>清理场景下额外的敏感片段（密钥、浏览器登录态、AI 会话记录等）。</summary>
        public static bool IsSensitiveSegment(string path)
        {
            string p = Norm(path);
            foreach (string seg in CleanSensitiveSegments)
            {
                string s = seg.ToLowerInvariant();
                if (p.Contains("\\" + s + "\\") || p.EndsWith("\\" + s)) return true;
            }
            return false;
        }

        public static bool IsWhitelisted(string path)
        {
            string p = Norm(path);
            List<string> list = WhiteList();
            foreach (string raw in list)
            {
                string entry = Norm(raw);
                if (entry.Length == 0) continue;
                bool wild = entry.Contains("*") || entry.Contains("?");
                if (wild)
                {
                    string prefix = entry;
                    int star = prefix.IndexOfAny(new char[] { '*', '?' });
                    string fixedPart = star > 0 ? prefix.Substring(0, star) : "";
                    if (fixedPart.Length >= 4 && (p.StartsWith(fixedPart) || fixedPart.StartsWith(p))) return true;
                    continue;
                }
                if (p == entry || p.StartsWith(entry + "\\")) return true;
            }
            return false;
        }

        /// <summary>综合判定：能否删除。</summary>
        public static bool CanDelete(string path, bool cleanMode)
        {
            if (IsProtected(path)) return false;
            if (IsWhitelisted(path)) return false;
            if (cleanMode && IsSensitiveSegment(path)) return false;
            string p = Norm(path);
            if (p.Length < 8) return false;
            if (p.EndsWith(":\\")) return false;
            return true;
        }
    }
}
