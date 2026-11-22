using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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

        /// <summary>回收站体积统计：逐块固定盘查询，整体加 8 秒超时——个别磁盘（外置盘、休眠中的机械盘）会让 shell 接口卡住。</summary>
        public static bool RecycleBinInfo(ref long size, ref int items)
        {
            long[] totalSize = new long[] { 0 };
            int[] totalItems = new int[] { 0 };
            bool[] ok = new bool[] { false };
            Thread worker = new Thread(delegate ()
            {
                try
                {
                    DriveInfo[] drives = DriveInfo.GetDrives();
                    for (int i = 0; i < drives.Length; i++)
                    {
                        DriveInfo d = drives[i];
                        try
                        {
                            if (d.DriveType != DriveType.Fixed) continue;
                            if (!d.IsReady) continue;
                            string root = d.RootDirectory.FullName;
                            if (!Directory.Exists(Path.Combine(root, "$Recycle.Bin"))) continue;
                            SHQUERYRBINFO info = new SHQUERYRBINFO();
                            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                            int hr = SHQueryRecycleBin(root, ref info);
                            if (hr != 0) continue;
                            lock (totalSize) { totalSize[0] += info.i64Size; }
                            lock (totalItems) { totalItems[0] += (int)info.i64NumItems; }
                            ok[0] = true;
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            });
            worker.IsBackground = true;
            try { worker.SetApartmentState(ApartmentState.STA); }
            catch (Exception) { }
            worker.Start();
            try { worker.Join(8000); }
            catch (Exception) { }
            lock (totalSize) { size = totalSize[0]; }
            lock (totalItems) { items = totalItems[0]; }
            return ok[0];
        }

        public static bool EmptyRecycleBin()
        {
            // 第一级：直接清 $Recycle.Bin 里的 $R/$I 数据（可绕过 shell 卡死，也能逐项汇报失败）
            bool fsOk = false;
            try
            {
                Thread t1 = new Thread(delegate () { fsOk = EmptyRecycleByFs(); });
                t1.IsBackground = true;
                t1.Start();
                t1.Join(6000);
            }
            catch (Exception) { }

            if (RecycleEmpty()) return true;
            if (fsOk) return false; // 自己删完了但复核仍非空，说明有被占用的项

            // 第二级：交给 shell（部分机器上只有它能清掉受保护项），5 秒没回来就当失败，不再等
            Thread t2 = new Thread(delegate ()
            {
                try { SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch (Exception) { }
            });
            t2.IsBackground = true;
            try { t2.SetApartmentState(ApartmentState.STA); } catch (Exception) { }
            t2.Start();
            try { t2.Join(5000); } catch (Exception) { }

            return RecycleEmpty();
        }

        private static bool EmptyRecycleByFs()
        {
            bool ok = true;
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        string rb = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin");
                        if (!Directory.Exists(rb)) continue;
                        string[] sids;
                        try { sids = Directory.GetDirectories(rb); }
                        catch (Exception) { continue; }
                        for (int s = 0; s < sids.Length; s++)
                        {
                            string sid = sids[s];
                            string[] entries;
                            try { entries = Directory.GetFileSystemEntries(sid); }
                            catch (Exception) { ok = false; continue; }
                            for (int e = 0; e < entries.Length; e++)
                            {
                                string ent = entries[e];
                                string name = Path.GetFileName(ent);
                                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                                if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) &&
                                    !name.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) continue;
                                try
                                {
                                    if (Directory.Exists(ent)) Directory.Delete(ent, true);
                                    else File.Delete(ent);
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        FileAttributes attr = File.GetAttributes(ent);
                                        File.SetAttributes(ent, attr & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
                                        if (Directory.Exists(ent)) Directory.Delete(ent, true);
                                        else File.Delete(ent);
                                    }
                                    catch (Exception) { ok = false; }
                                }
                            }
                        }
                    }
                    catch (Exception) { ok = false; }
                }
            }
            catch (Exception) { return false; }
            return ok;
        }

        /// <summary>复核回收站是否真的空了（逐固定盘查询，3 秒超时）。</summary>
        private static bool RecycleEmpty()
        {
            bool empty = true;
            long[] size = new long[] { 0 };
            bool[] done = new bool[] { false };
            Thread worker = new Thread(delegate ()
            {
                try
                {
                    DriveInfo[] drives = DriveInfo.GetDrives();
                    for (int i = 0; i < drives.Length; i++)
                    {
                        try
                        {
                            if (drives[i].DriveType != DriveType.Fixed || !drives[i].IsReady) continue;
                            string root = drives[i].RootDirectory.FullName;
                            if (!Directory.Exists(Path.Combine(root, "$Recycle.Bin"))) continue;
                            SHQUERYRBINFO info = new SHQUERYRBINFO();
                            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                            if (SHQueryRecycleBin(root, ref info) != 0) continue;
                            lock (size) { size[0] += info.i64Size; }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
                done[0] = true;
            });
            worker.IsBackground = true;
            try { worker.SetApartmentState(ApartmentState.STA); }
            catch (Exception) { }
            worker.Start();
            try { worker.Join(3000); } catch (Exception) { }
            if (!done[0]) return false; // 查询卡住时不能当成已清空
            lock (size) { if (size[0] > 0) empty = false; }
            return empty;
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

