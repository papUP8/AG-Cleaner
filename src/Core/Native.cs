using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace AGCleaner.Core
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLongPathNameW(string shortPath, StringBuilder longPath, int length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileExW(string existing, string target, int flags);

        /// <summary>被占用的文件登记为“重启后删除”，下次开机释放空间。</summary>
        public static bool DelayDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) return false;
                if (!File.Exists(path)) return true;
                return MoveFileExW(path, null, 4); // MOVEFILE_DELAY_UNTIL_REBOOT
            }
            catch (Exception) { return false; }
        }

        // 把 8.3 短路径（C:\Users\ADMINI~1\...）展开为长路径，避免同一目录被算成两个
        public static string LongPath(string path)
        {
            if (path == null || path.Length == 0) return path;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = GetLongPathNameW(path, sb, sb.Capacity);
                if (n <= 0) return path;
                if (n > sb.Capacity)
                {
                    sb = new StringBuilder(n);
                    n = GetLongPathNameW(path, sb, sb.Capacity);
                    if (n <= 0) return path;
                }
                return sb.ToString();
            }
            catch (Exception) { return path; }
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        public static MEMORYSTATUSEX Memory()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            try { GlobalMemoryStatusEx(m); }
            catch (Exception) { }
            return m;
        }

        /// <summary>把一组路径移入回收站。</summary>
        public static bool Recycle(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return true;
            StringBuilder sb = new StringBuilder();
            foreach (string p in paths)
            {
                sb.Append(p);
                sb.Append('\0');
            }
            sb.Append('\0');
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.wFunc = 3; // FO_DELETE
            op.pFrom = sb.ToString();
            op.pTo = null;
            op.fFlags = (ushort)(0x0040 | 0x0010 | 0x0004 | 0x0400); // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
            try
            {
                return SHFileOperation(ref op) == 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>以管理员身份重新启动本程序。</summary>
        public static bool RelaunchAsAdmin()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application_ExePath();
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Path.GetDirectoryName(psi.FileName);
                Process.Start(psi);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static string Application_ExePath()
        {
            try
            {
                Process p = Process.GetCurrentProcess();
                return p.MainModule.FileName;
            }
            catch (Exception)
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AG-Cleaner.exe");
            }
        }

        /// <summary>打开资源管理器并选中某路径。</summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch (Exception) { }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception) { }
        }

        public static bool Has(string exe)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.Arguments = "/?";
                Process p = Process.Start(psi);
                p.WaitForExit(3000);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
