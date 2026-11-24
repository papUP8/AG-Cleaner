using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace AGCleaner.Core
{
    /// <summary>
    /// 用 Windows 的重启管理器（Restart Manager）查出“到底是哪个程序占着这些文件”。
    /// 只做查询，不关闭、不结束任何进程，纯粹用来在完成页里解释“为什么没删干净”。
    /// </summary>
    public static class Locker
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public long ProcessStartTime;
        }

        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, IntPtr rgApplications, uint nServices, IntPtr rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            IntPtr rgAffectedApps, ref uint lpdwRebootReasons);

        /// <summary>查这些文件现在被哪些程序占着，返回去重后的程序名（最多 maxNames 个）。</summary>
        public static List<string> Who(IList<string> files, int maxNames)
        {
            List<string> names = new List<string>();
            if (files == null || files.Count == 0) return names;
            string[] arr = new string[files.Count];
            for (int i = 0; i < files.Count; i++) arr[i] = files[i];

            uint session = 0;
            string key = Guid.NewGuid().ToString();
            if (RmStartSession(out session, 0, key) != 0) return names;
            try
            {
                if (RmRegisterResources(session, (uint)arr.Length, arr, 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return names;
                uint needed = 0, count = 0, reason = 0;
                // 第一次调用故意传 0 个槽位，只问“需要几个”，返回 ERROR_MORE_DATA 是正常现象
                RmGetList(session, out needed, ref count, IntPtr.Zero, ref reason);
                if (needed == 0) return names;
                count = needed;
                int sz = Marshal.SizeOf(typeof(RM_PROCESS_INFO));
                IntPtr buf = Marshal.AllocHGlobal((int)count * sz);
                try
                {
                    if (RmGetList(session, out needed, ref count, buf, ref reason) != 0) return names;
                    for (int i = 0; i < (int)count; i++)
                    {
                        RM_PROCESS_INFO pi = (RM_PROCESS_INFO)Marshal.PtrToStructure(
                            (IntPtr)((long)buf + i * sz), typeof(RM_PROCESS_INFO));
                        string nm = (pi.strAppName ?? "").Trim();
                        // 进程名比 RM 回来的显示名更干净（显示名常常带路径或被截断），优先用 PID 反查
                        try
                        {
                            if (pi.Process.dwProcessId > 0)
                            {
                                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pi.Process.dwProcessId))
                                {
                                    if (p != null && p.ProcessName.Length > 0) nm = p.ProcessName;
                                }
                            }
                        }
                        catch (Exception) { }
                        if (nm.Length == 0) nm = "PID " + pi.Process.dwProcessId;
                        if (nm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) nm = nm.Substring(0, nm.Length - 4);
                        if (string.Equals(nm, "AG-Cleaner", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!names.Contains(nm)) names.Add(nm);
                        if (names.Count >= maxNames) break;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception) { }
            finally { try { RmEndSession(session); } catch (Exception) { } }
            return names;
        }

        /// <summary>
        /// 从没删干净的路径里取一批残留文件（每个目录最多 maxFiles 个），查出占用者。
        /// 目录可能很大，所以只抽样，用于给用户一个“是谁占着”的答案，不做穷举。
        /// </summary>
        public static List<string> WhoForPath(string path, int maxFiles)
        {
            List<string> sample = new List<string>();
            try
            {
                if (File.Exists(path))
                {
                    sample.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    Collect(path, sample, maxFiles, 0);
                }
            }
            catch (Exception) { }
            return Who(sample, 4);
        }

        private static void Collect(string dir, List<string> into, int maxFiles, int depth)
        {
            if (into.Count >= maxFiles || depth > 6) return;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { files = new string[0]; }
            for (int i = 0; i < files.Length; i++)
            {
                if (into.Count >= maxFiles) return;
                into.Add(files[i]);
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { subs = new string[0]; }
            for (int i = 0; i < subs.Length; i++)
            {
                if (into.Count >= maxFiles) return;
                Collect(subs[i], into, maxFiles, depth + 1);
            }
        }
    }
}
