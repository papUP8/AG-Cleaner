using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CCDesk.Core
{
    public class StartupItem
    {
        public string Name = "";
        public string Command = "";
        public string Source = "";
        public bool Enabled = true;
        public bool Machine = false;
        public bool IsFile = false;
        public string KeyPath = "";
        public string ValueName = "";
        public string FilePath = "";
        public string HiveName = "HKCU";

        public string TypeText { get { return IsFile ? "启动文件夹" : "注册表"; } }
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
            ReadRunKey(Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", false, items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", true, items);
            ReadRunKey(Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true, items);

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
                    it.Enabled = true;
                    it.Machine = f == 1;
                    it.Source = f == 0 ? "用户启动文件夹" : "公共启动文件夹";
                    items.Add(it);
                }
            }

            items.Sort(delegate (StartupItem a, StartupItem b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return items;
        }

        private static void ReadRunKey(RegistryKey hive, string hiveName, string keyPath, bool machine, List<StartupItem> items)
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
                            it.Enabled = true;
                            it.Machine = machine;
                            it.HiveName = hiveName;
                            it.KeyPath = keyPath;
                            it.ValueName = names[i];
                            it.Source = machine ? "HKLM Run" : "HKCU Run";
                            if (it.Name.Length == 0) it.Name = "未命名启动项";
                            items.Add(it);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static List<string> DisabledNames()
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(BackupFile)) return list;
                string[] lines = File.ReadAllLines(BackupFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length >= 5) list.Add(p[1] + "|" + p[4]);
                }
            }
            catch (Exception) { }
            return list;
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
                    it.Source = p[0] == "file" ? "启动文件夹（已备份）" : (p[2] + " Run");
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
            if (it.IsFile)
            {
                try
                {
                    string dest = Path.Combine(BackupDir, Path.GetFileName(it.FilePath));
                    if (File.Exists(dest)) dest = dest + "." + DateTime.Now.Ticks;
                    File.Move(it.FilePath, dest);
                    AppendBackup("file", it.HiveName, "", "", dest, it.FilePath, Encoding.UTF8.GetBytes(it.FilePath));
                    OpLog.Write("startup", "禁用 " + it.Name, 0);
                    message = "已从启动文件夹移除（文件已备份）";
                    return true;
                }
                catch (Exception ex) { message = "操作失败：" + ex.Message; return false; }
            }
            try
            {
                RegistryKey hive = it.HiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using (RegistryKey k = hive.OpenSubKey(it.KeyPath, true))
                {
                    if (k == null) { message = "无法打开注册表项"; return false; }
                    object val = k.GetValue(it.ValueName);
                    RegistryValueKind kind = k.GetValueKind(it.ValueName);
                    byte[] data;
                    if (val is byte[]) data = (byte[])val;
                    else if (val is string[]) data = Encoding.UTF8.GetBytes(string.Join("\n", (string[])val));
                    else data = Encoding.UTF8.GetBytes(val == null ? "" : val.ToString());
                    AppendBackup("reg", it.HiveName, it.KeyPath, it.ValueName, kind.ToString(), "", data);
                    k.DeleteValue(it.ValueName, false);
                    OpLog.Write("startup", "禁用 " + it.Name, 0);
                    message = "已禁用（已备份，可随时还原）";
                    return true;
                }
            }
            catch (Exception ex) { message = "操作失败：" + ex.Message; return false; }
        }

        public static bool Enable(StartupItem it, out string message)
        {
            message = "";
            try
            {
                if (it.IsFile)
                {
                    if (File.Exists(it.FilePath)) { message = "已存在"; return true; }
                    string src = Path.Combine(BackupDir, Path.GetFileName(it.FilePath));
                    if (!File.Exists(src)) { message = "备份文件不存在"; return false; }
                    File.Copy(src, it.FilePath, true);
                    RemoveBackup("file", it.FilePath, "");
                    OpLog.Write("startup", "启用 " + it.Name, 0);
                    message = "已恢复到启动文件夹";
                    return true;
                }
                RegistryKey hive = it.HiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using (RegistryKey k = hive.OpenSubKey(it.KeyPath, true))
                {
                    if (k == null) { message = "无法打开注册表项"; return false; }
                    byte[] data = ReadBackupData(it.ValueName, it.FilePath, it.KeyPath);
                    if (data == null) { message = "找不到备份数据"; return false; }
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
                    OpLog.Write("startup", "启用 " + it.Name, 0);
                    message = "已重新启用";
                    return true;
                }
            }
            catch (Exception ex) { message = "操作失败：" + ex.Message; return false; }
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
