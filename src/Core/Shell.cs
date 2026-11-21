using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AGCleaner.Core
{
    public static class Shell
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public static bool RecycleBinInfo(ref long size, ref int items)
        {
            try
            {
                SHQUERYRBINFO info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(null, ref info);
                size = info.i64Size;
                items = (int)info.i64NumItems;
                return hr == 0;
            }
            catch (Exception) { return false; }
        }

        public static bool EmptyRecycleBin()
        {
            try
            {
                // NOCONFIRMATION | NOPROGRESSUI | NOSOUND
                return SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>操作日志（TSV），用于“历史记录”页与审计。</summary>
    public static class OpLog
    {
        public class Entry
        {
            public DateTime Time;
            public string Action = "";
            public string Path = "";
            public long Bytes;
        }

        public static void Write(string action, string path, long bytes)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + action + "\t" + bytes + "\t" + path + "\r\n";
                File.AppendAllText(Store.OpLogFile, line, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        public static List<Entry> Read(int max)
        {
            List<Entry> list = new List<Entry>();
            try
            {
                if (!File.Exists(Store.OpLogFile)) return list;
                string[] lines = File.ReadAllLines(Store.OpLogFile, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
                {
                    string l = lines[i];
                    if (l.Length == 0) continue;
                    string[] parts = l.Split('\t');
                    if (parts.Length < 4) continue;
                    Entry e = new Entry();
                    try { e.Time = DateTime.Parse(parts[0]); } catch (Exception) { }
                    e.Action = parts[1];
                    try { e.Bytes = long.Parse(parts[2]); } catch (Exception) { }
                    e.Path = parts[3];
                    list.Add(e);
                }
            }
            catch (Exception) { }
            return list;
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(Store.OpLogFile)) File.Delete(Store.OpLogFile);
            }
            catch (Exception) { }
        }

        public static string ActionLabel(string action)
        {
            if (action == "clean") return Lang.T("清理");
            if (action == "emptied") return Lang.T("清空回收站");
            if (action == "uninstall") return Lang.T("卸载");
            if (action == "leftover") return Lang.T("清理残留");
            if (action == "startup") return Lang.T("启动项");
            if (action == "optimize") return Lang.T("系统优化");
            if (action == "delete") return Lang.T("删除文件");
            return action;
        }
    }
}
