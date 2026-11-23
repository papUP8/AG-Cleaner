using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        /// <summary>
        /// 回收站体积统计：逐块固定盘各自起线程查询，单盘最多等 1.2 秒。
        /// 不用 SHQueryRecycleBin("") 汇总整机——实测本机它会把所有卷（含慢速卷）全走一遍，
        /// 121 秒才返回；单盘查询只要 7 毫秒。卡住的单盘直接放弃，不拖累整轮扫描。
        /// </summary>
        public static bool RecycleBinInfo(ref long size, ref int items)
        {
            long totalSize = 0;
            int totalItems = 0;
            bool any = false;
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (Exception) { return false; }
            for (int i = 0; i < drives.Length; i++)
            {
                DriveInfo d = drives[i];
                try
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    string root = d.RootDirectory.FullName;
                    if (!Directory.Exists(Path.Combine(root, "$Recycle.Bin"))) continue;
                    long s = 0;
                    int n = 0;
                    if (QueryOneDrive(root, ref s, ref n, 1200))
                    {
                        totalSize += s;
                        totalItems += n;
                        any = true;
                    }
                }
                catch (Exception) { }
            }
            size = totalSize;
            items = totalItems;
            return any;
        }

        /// <summary>单块盘的回收站查询，独立超时；超时不等待结果，直接当这块盘没查到。</summary>
        private static bool QueryOneDrive(string root, ref long size, ref int items, int timeoutMs)
        {
            long[] sz = new long[] { 0 };
            int[] cnt = new int[] { 0 };
            bool[] ok = new bool[] { false };
            Thread t = new Thread(delegate ()
            {
                try
                {
                    SHQUERYRBINFO info = new SHQUERYRBINFO();
                    info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                    if (SHQueryRecycleBin(root, ref info) != 0) return;
                    lock (sz) { sz[0] = info.i64Size; }
                    lock (cnt) { cnt[0] = (int)info.i64NumItems; }
                    ok[0] = true;
                }
                catch (Exception) { }
            });
            t.IsBackground = true;
            try { t.Start(); }
            catch (Exception) { return false; }
            try { t.Join(timeoutMs); }
            catch (Exception) { }
            if (!ok[0]) return false;
            size += sz[0];
            items += cnt[0];
            return true;
        }

        public static bool EmptyRecycleBin()
        {
            int hr;
            return EmptyRecycleBin(out hr);
        }

        /// <summary>
        /// 清空回收站。三级尝试：直接删 $R/$I → 资源管理器「清空」动词 → shell API。
        /// <paramref name="hr"/> 返回 shell API 的错误码（成功 0，被拦截为负数，没走到这一步为 -1），
        /// 让调用方能把真实 HRESULT 写进日志，而不是只报一句“失败了”。
        /// </summary>
        public static bool EmptyRecycleBin(out int hr)
        {
            hr = -1;
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

            // 第二级：走资源管理器的 shell 动词「清空回收站」。
            // 安全软件对 $Recycle.Bin 的目录级保护会拦直接删，但放行 explorer 自己发起的清空，
            // 360、电脑管家、FluentCleaner 能清空也是这个路数。
            bool verbOk = false;
            try
            {
                Thread t3 = new Thread(delegate () { verbOk = EmptyRecycleByShellVerb(); });
                t3.IsBackground = true;
                try { t3.SetApartmentState(ApartmentState.STA); } catch (Exception) { }
                t3.Start();
                t3.Join(8000);
            }
            catch (Exception) { }
            if (RecycleEmpty()) return true;

            // 第三级：交给 shell API，成败以返回的 HRESULT 为准；5 秒没回来就当失败，不再等
            int[] shellHr = new int[] { unchecked((int)0x80004005) };
            Thread t2 = new Thread(delegate ()
            {
                try { shellHr[0] = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch (Exception) { }
            });
            t2.IsBackground = true;
            try { t2.SetApartmentState(ApartmentState.STA); } catch (Exception) { }
            t2.Start();
            try { t2.Join(5000); } catch (Exception) { }

            hr = shellHr[0];
            if (hr == 0) return true;
            // shell 报了错，但偶尔仍会清掉一部分，复核一次再下结论
            return RecycleEmpty();
        }

        /// <summary>用 Shell.Application 拿到回收站的「清空」动词并执行（动词名是本地化的，按关键字匹配）。</summary>
        private static bool EmptyRecycleByShellVerb()
        {
            object app = null, folder = null, self = null, verbs = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) { Dbg("no progid"); return false; }
                app = Activator.CreateInstance(t);
                folder = t.InvokeMember("Namespace", BindingFlags.InvokeMethod, null, app, new object[] { 10 });
                if (folder == null) { Dbg("no folder"); return false; }
                Type fType = folder.GetType();
                self = fType.InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
                if (self == null) { Dbg("no self"); return false; }
                verbs = self.GetType().InvokeMember("Verbs", BindingFlags.InvokeMethod, null, self, null);
                if (verbs == null) { Dbg("no verbs"); return false; }
                int count = (int)verbs.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, verbs, null);
                Dbg("verbs=" + count);
                for (int i = 0; i < count; i++)
                {
                    object v = verbs.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, verbs, new object[] { i });
                    if (v == null) continue;
                    string name = (string)v.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, v, null);
                    Dbg("verb[" + i + "]=" + name);
                    if (name == null || !IsEmptyVerb(name)) continue;
                    v.GetType().InvokeMember("DoIt", BindingFlags.InvokeMethod, null, v, null);
                    Dbg("DoIt ok");
                    return true;
                }
            }
            catch (Exception ex) { Dbg("ex: " + ex.GetType().Name + " " + ex.Message); return false; }
            return false;
        }

        private static void Dbg(string s)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ag-rb-verb.txt"), s + "\r\n"); }
            catch (Exception) { }
        }

        /// <summary>各语言下「清空回收站」动词可能的写法。</summary>
        private static bool IsEmptyVerb(string name)
        {
            string low = name.ToLowerInvariant();
            string[] keys = new string[] { "清空", "empty", "leeren", "vider", "очист", "vaciar", "emptying" };
            for (int i = 0; i < keys.Length; i++) if (low.Contains(keys[i])) return true;
            return false;
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

