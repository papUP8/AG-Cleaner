using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace CCDesk.Core
{
    public class ProcInfo
    {
        public string Name = "";
        public double Cpu = 0;
        public long Memory = 0;
        public int Pid = 0;
    }

    public class Sample
    {
        public double CpuPercent = 0;
        public double MemPercent = 0;
        public long MemUsed = 0;
        public long MemTotal = 0;
        public long DiskUsed = 0;
        public long DiskTotal = 0;
        public double DiskPercent = 0;
        public long NetDown = 0;
        public long NetUp = 0;
        public int ProcCount = 0;
        public List<ProcInfo> Top = new List<ProcInfo>();
        public DateTime Time = DateTime.Now;
    }

    /// <summary>实时指标采样器（CPU/内存/磁盘/网络/进程）。</summary>
    public class Sampler
    {
        private Dictionary<int, TimeSpan> _lastCpu = new Dictionary<int, TimeSpan>();
        private DateTime _lastTime = DateTime.MinValue;
        private long _lastRx = 0;
        private long _lastTx = 0;
        private bool _first = true;

        public Sample Next()
        {
            Sample s = new Sample();
            DateTime now = DateTime.Now;
            double elapsed = _lastTime == DateTime.MinValue ? 0 : (now - _lastTime).TotalMilliseconds;

            Native.MEMORYSTATUSEX m = Native.Memory();
            s.MemTotal = (long)m.ullTotalPhys;
            s.MemUsed = (long)(m.ullTotalPhys - m.ullAvailPhys);
            s.MemPercent = m.dwMemoryLoad;

            string sysDrive = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            s.DiskTotal = Disk.TotalSpace(sysDrive);
            s.DiskUsed = s.DiskTotal - Disk.FreeSpace(sysDrive);
            s.DiskPercent = s.DiskTotal > 0 ? (double)s.DiskUsed / s.DiskTotal * 100 : 0;

            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch (Exception) { procs = new Process[0]; }
            s.ProcCount = procs.Length;

            Dictionary<int, TimeSpan> current = new Dictionary<int, TimeSpan>();
            Dictionary<int, ProcInfo> map = new Dictionary<int, ProcInfo>();
            double busy = 0;
            for (int i = 0; i < procs.Length; i++)
            {
                Process p = procs[i];
                try
                {
                    int pid = p.Id;
                    TimeSpan t = p.TotalProcessorTime;
                    current[pid] = t;
                    ProcInfo info = new ProcInfo();
                    info.Pid = pid;
                    info.Name = p.ProcessName;
                    try { info.Memory = p.WorkingSet64; } catch (Exception) { }
                    if (!_first && elapsed > 200)
                    {
                        TimeSpan prev;
                        if (_lastCpu.TryGetValue(pid, out prev))
                        {
                            double ms = (t - prev).TotalMilliseconds;
                            double pct = ms / elapsed * 100.0 / Environment.ProcessorCount;
                            if (pct < 0) pct = 0;
                            info.Cpu = pct;
                            busy += pct;
                        }
                    }
                    map[pid] = info;
                }
                catch (Exception) { }
                finally
                {
                    try { p.Dispose(); } catch (Exception) { }
                }
            }
            _lastCpu = current;

            if (_first)
            {
                busy = 0;
            }
            s.CpuPercent = Math.Max(0, Math.Min(100, busy));
            if (_first) s.CpuPercent = 0;

            List<ProcInfo> list = new List<ProcInfo>();
            foreach (KeyValuePair<int, ProcInfo> kv in map) list.Add(kv.Value);
            list.Sort(delegate (ProcInfo a, ProcInfo b)
            {
                double am = a.Cpu * 4 + (double)a.Memory / (1024.0 * 1024.0);
                double bm = b.Cpu * 4 + (double)b.Memory / (1024.0 * 1024.0);
                return bm.CompareTo(am);
            });
            for (int i = 0; i < list.Count && s.Top.Count < 6; i++) s.Top.Add(list[i]);

            long rx = 0, tx = 0;
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    try
                    {
                        if (nics[i].OperationalStatus != OperationalStatus.Up) continue;
                        if (nics[i].NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (nics[i].NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                        IPv4InterfaceStatistics st = nics[i].GetIPv4Statistics();
                        rx += st.BytesReceived;
                        tx += st.BytesSent;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }

            if (!_first && elapsed > 200)
            {
                s.NetDown = (long)Math.Max(0, (rx - _lastRx) * 1000.0 / elapsed);
                s.NetUp = (long)Math.Max(0, (tx - _lastTx) * 1000.0 / elapsed);
            }
            _lastRx = rx;
            _lastTx = tx;
            _lastTime = now;
            _first = false;
            s.Time = now;
            return s;
        }

        /// <summary>健康评分与诊断建议。</summary>
        public static int Health(Sample s, int startupCount, out List<string> notes)
        {
            notes = new List<string>();
            int score = 100;
            double freePct = 100 - s.DiskPercent;
            if (s.DiskPercent > 90) { score -= 25; notes.Add(Lang.T("系统盘剩余空间不足 10%，建议立即清理")); }
            else if (s.DiskPercent > 80) { score -= 12; notes.Add(Lang.T("系统盘使用率超过 80%，建议清理缓存")); }
            else if (s.DiskPercent > 70) { score -= 5; }

            if (s.MemPercent > 90) { score -= 15; notes.Add(Lang.T("内存占用超过 90%，请关闭后台程序")); }
            else if (s.MemPercent > 80) { score -= 8; notes.Add(Lang.T("内存占用偏高")); }

            if (s.CpuPercent > 85) { score -= 10; notes.Add(Lang.T("CPU 负载很高，检查占用最高的进程")); }
            else if (s.CpuPercent > 70) { score -= 4; }

            if (startupCount > 20) { score -= 8; notes.Add(Lang.T("开机启动项偏多（") + startupCount + Lang.T(" 项），建议精简")); }
            else if (startupCount > 10) { score -= 4; notes.Add(Lang.T("开机启动项较多（") + startupCount + Lang.T(" 项）")); }

            if (notes.Count == 0) notes.Add(Lang.T("系统状态良好，保持定期清理即可"));
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            return score;
        }
    }
}
