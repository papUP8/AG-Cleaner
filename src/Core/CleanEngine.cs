using System;
using System.Collections.Generic;
using System.IO;

namespace AGCleaner.Core
{
    public enum CleanKind { DirContents, DirItself, OldFilesInDir, OldFilesExt, RecycleBin, FileItself, OrphanGroup }

    public class CleanItem
    {
        public string CategoryId = "";
        public string Name = "";
        public string Path = "";
        public CleanKind Kind = CleanKind.DirContents;
        public int OlderThanDays = 0;
        public string Ext = "";
        public long Bytes = 0;
        public int Files = 0;
        public bool Recycle = false;
        /// <summary>高风险/不可逆项：扫描出来但不预先勾选，必须用户自己点。</summary>
        public bool OptIn = false;
        /// <summary>默认不勾，但不算高风险（例如重建代价较高的开发缓存）。</summary>
        public bool DefaultOff = false;
        /// <summary>界面上要用警示色标出来的高风险项（Cookie、回收站、旧系统备份这类删了回不来的）。</summary>
        public bool HighRisk = false;
        public bool AdminNeeded = false;
        /// <summary>清单上要给用户看的影响说明（Cookie 这类删了有代价的项），多行用 \n 分隔。</summary>
        public string Impact = "";
        public string Note = "";
        public long MinBytes = 0;
        public bool Exists = false;
        /// <summary>界面勾选状态，缓存在清单项上，切换筛选/排序时不丢。</summary>
        public bool Checked = false;
        public bool CheckInit = false;
        /// <summary>OrphanGroup 类型时挂载的残留分组。</summary>
        public object Data = null;
    }

    public class CleanCategory
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public string Glyph = "";
        public bool DefaultOn = true;
        public bool AdminOnly = false;
        public List<CleanItem> Items = new List<CleanItem>();

        public long TotalBytes()
        {
            long t = 0;
            for (int i = 0; i < Items.Count; i++) t += Items[i].Bytes;
            return t;
        }
    }

    public static class FileUtil
    {
        /// <summary>体积统计阶段的可选进度钩子：每处理若干文件回调一次，用于大目录里的“活着”提示。</summary>
        public static Action<long> SizeHook;

        [ThreadStatic] private static int _hookFiles;

        private static void HookTick(long total)
        {
            Action<long> h = SizeHook;
            if (h == null) return;
            _hookFiles++;
            if ((_hookFiles & 63) != 0) return;
            try { h(total); }
            catch (Exception) { }
        }

        /// <summary>递归统计目录体积（不跟随符号链接/联接点）。</summary>
        public static long SizeOf(string path)
        {
            long total = 0;
            int files = 0;
            SizeOf(path, ref total, ref files, 0);
            return total;
        }

        public static void SizeOf(string path, ref long total, ref int files, int depth)
        {
            if (depth > 40) return;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (File.Exists(path))
                {
                    total += fi.Length;
                    files++;
                    HookTick(total);
                    return;
                }
            }
            catch (Exception) { return; }

            if (!Directory.Exists(path)) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i];
                try
                {
                    FileAttributes attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        SizeOf(e, ref total, ref files, depth + 1);
                    }
                    else
                    {
                        total += new FileInfo(e).Length;
                        files++;
                        HookTick(total);
                    }
                }
                catch (Exception) { }
            }
        }

        public static void SizeOfOld(string path, DateTime cutoff, ref long total, ref int files, int depth)
        {
            if (depth > 40) return;
            if (!Directory.Exists(path)) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i];
                try
                {
                    FileAttributes attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        SizeOfOld(e, cutoff, ref total, ref files, depth + 1);
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(e);
                        if (fi.LastWriteTime < cutoff)
                        {
                            total += fi.Length;
                            files++;
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        public static void ClearReadOnly(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                try
                {
                    FileAttributes attr = File.GetAttributes(path);
                    File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception) { }
            }
        }

        /// <summary>删除目录内的条目，保留目录本身。</summary>
        public static long DeleteContents(string dir, bool recycle, ref int errors)
        {
            return DeleteContents(dir, recycle, ref errors, null);
        }

        /// <summary>删除目录内的条目；sub(done,total) 用于把本条目的删除进度折算到整体百分比。</summary>
        public static long DeleteContents(string dir, bool recycle, ref int errors, Action<int, int> sub)
        {
            long freed = 0;
            if (!Directory.Exists(dir)) return 0;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { errors++; return 0; }

            List<string> safe = new List<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (Guard.CanDelete(entries[i], true)) safe.Add(entries[i]);
            }
            int total = Math.Max(1, safe.Count);
            if (sub != null) sub(0, total);
            if (recycle && Native.Recycle(safe))
            {
                for (int i = 0; i < safe.Count; i++)
                {
                    freed += SizeOf(safe[i]);
                    if (sub != null && (i & 3) == 3) sub(i + 1, total);
                }
                if (sub != null) sub(total, total);
                return freed;
            }
            for (int i = 0; i < safe.Count; i++)
            {
                long before = SizeOf(safe[i]);
                ClearReadOnly(safe[i]);
                // 真正删除：文件删不掉就记为未删，目录逐层删到能删的为止。
                // 以前这里会登记“重启后删除”，函数返回成功、体积也计入已释放，
                // 但文件其实还在盘上，重新扫描又出现同一批项目——那就是假清理。
                if (File.Exists(safe[i]))
                {
                    try { File.Delete(safe[i]); }
                    catch (Exception) { }
                }
                else if (Directory.Exists(safe[i]))
                {
                    RemoveDirBestEffort(safe[i], 0);
                }
                long after = File.Exists(safe[i]) || Directory.Exists(safe[i]) ? SizeOf(safe[i]) : 0;
                if (after < before) freed += before - after;
                if (after > 0) errors++;
                if (sub != null) sub(i + 1, total);
            }
            return freed;
        }

        /// <summary>逐层删除目录内容：被占用的单个文件跳过，其余照删，最后清掉空目录。</summary>
        private static void RemoveDirBestEffort(string dir, int depth)
        {
            if (depth > 12) return;
            string[] ents;
            try { ents = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < ents.Length; i++)
            {
                string e = ents[i];
                try
                {
                    if (!Guard.CanDelete(e, false)) continue;
                    ClearReadOnly(e);
                    if (Directory.Exists(e)) RemoveDirBestEffort(e, depth + 1);
                    else if (File.Exists(e))
                    {
                        try { File.Delete(e); }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            try { Directory.Delete(dir, false); }
            catch (Exception) { }
        }

        /// <summary>删除若干“早于 cutoff”的文件。</summary>
        public static long DeleteOldFiles(string dir, DateTime cutoff, bool recycle, ref int errors)
        {
            return DeleteOldFiles(dir, cutoff, recycle, ref errors, null);
        }

        public static long DeleteOldFiles(string dir, DateTime cutoff, bool recycle, ref int errors, Action<int, int> sub)
        {
            List<string> targets = new List<string>();
            long total = 0;
            CollectOldFiles(dir, cutoff, targets, ref total, 0);
            if (targets.Count == 0) return 0;
            if (sub != null) sub(0, Math.Max(1, targets.Count));
            if (recycle)
            {
                if (Native.Recycle(targets))
                {
                    if (sub != null) sub(targets.Count, targets.Count);
                    return total;
                }
            }
            long freed = 0;
            int n = Math.Max(1, targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                long before = 0;
                try { before = new FileInfo(targets[i]).Length; } catch (Exception) { }
                ClearReadOnly(targets[i]);
                if (File.Exists(targets[i]))
                {
                    try { File.Delete(targets[i]); }
                    catch (Exception) { }
                }
                if (!File.Exists(targets[i])) freed += before;
                else errors++;
                if (sub != null) sub(i + 1, n);
            }
            return freed;
        }

        private static void CollectOldFiles(string dir, DateTime cutoff, List<string> targets, ref long total, int depth)
        {
            if (depth > 12) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i];
                try
                {
                    FileAttributes attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        // 只删除久未修改的子目录（整目录回收），保留仍在被写入的目录
                        DateTime dirTime = Directory.GetLastWriteTime(e);
                        if (dirTime < cutoff)
                        {
                            targets.Add(e);
                            total += SizeOf(e);
                        }
                        else
                        {
                            CollectOldFiles(e, cutoff, targets, ref total, depth + 1);
                        }
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(e);
                        if (fi.LastWriteTime < cutoff)
                        {
                            targets.Add(e);
                            total += fi.Length;
                        }
                    }
                }
                catch (Exception) { }
            }
        }
    }

    /// <summary>扫描/清理进度回调：百分比 0-100 与当前动作说明。</summary>
    public delegate void StepProgress(int percent, string text);

    /// <summary>按分类目录逐项扫描，产出“审阅后删除”的清单。</summary>
    public static class CleanEngine
    {
        private static void Report(StepProgress p, int percent, string text)
        {
            if (p == null) return;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            p(percent, text);
        }

        // ---------------- 细粒度进度 ----------------
        // 以前只有“类目扫完一个才动一格”，慢类目跑一分钟进度就钉在 33% 不动；
        // 这里给每个类目一个按权重占的切片，类目内部再按子步骤推进（扫到第几个浏览器、
        // 第几个应用目录、残留扫描走到第几阶段），折算成总进度上的连续小步。

        /// <summary>一个扫描类目在总进度里的切片。</summary>
        private class JobSlice
        {
            public string Key = "";
            public double Weight = 1;
            public double Frac = 0;      // 本类目内部完成度 0..1
            public int Done = 0;
            public int Expect = 0;       // 0 表示总量未知，用饱和曲线估算
            public long StartMs = 0;     // 本类目开始扫描的时刻，进度按“已跑时间/预计耗时”推进
            public bool Finished = false;
            public string Note = "";
            public bool Active = true;
        }

        [ThreadStatic] private static JobSlice _slice;

        /// <summary>声明本类目大致有多少个子步骤（已知循环次数时用，进度更准）。</summary>
        internal static void SliceExpect(int total)
        {
            JobSlice s = _slice;
            if (s == null || total <= 0) return;
            s.Expect = total;
        }

        /// <summary>推进一个子步骤。step 为本次完成的工作量，note 为界面上要显示的动作。</summary>
        internal static void SliceTick(int step, string note)
        {
            JobSlice s = _slice;
            if (s == null) return;
            if (step < 1) step = 1;
            s.Done += step;
            if (note != null && note.Length > 0) s.Note = note;
            double f = s.Expect > 0 ? (double)s.Done / s.Expect : (double)s.Done / (s.Done + 8.0);
            if (f > 0.94) f = 0.94; // 最后 6% 留给类目真正收尾，避免“显示满了其实还在跑”
            if (f > s.Frac) s.Frac = f;
            if (_hub != null) _hub.Report();
        }

        internal static void SliceTick(string note)
        {
            SliceTick(1, note);
        }

        internal static void SliceNote(string note)
        {
            JobSlice s = _slice;
            if (s == null || note == null || note.Length == 0) return;
            s.Note = note;
        }

        /// <summary>直接设定本类目的完成度（0..1），用于已有百分比回调的子扫描（如残留扫描）。</summary>
        internal static void SliceSet(double f, string note)
        {
            JobSlice s = _slice;
            if (s == null) return;
            if (note != null && note.Length > 0) s.Note = note;
            if (f < 0) f = 0;
            if (f > 0.94) f = 0.94;
            if (f > s.Frac) s.Frac = f;
            if (_hub != null) _hub.Report();
        }

        /// <summary>把若干切片按权重合成总百分比，并做节流（150 ms 或百分比变化才上报）。</summary>
        private class Hub
        {
            public List<JobSlice> Slices = new List<JobSlice>();
            public StepProgress Prog;
            public double Lo = 3;
            public double Hi = 22;
            public long T0 = 0;            // 发现阶段的起点
            public double PredTotal = 0;   // 上次发现阶段的总耗时（秒），用于给整体节奏兜底
            private int _lastPct = -1;
            private long _lastMs = 0;

            public void Report()
            {
                lock (Slices)
                {
                    double w = 0, f = 0;
                    JobSlice noteSrc = null;
                    double noteRemain = -1;
                    long now = Environment.TickCount;
                    for (int i = 0; i < Slices.Count; i++)
                    {
                        JobSlice s = Slices[i];
                        if (s == null || !s.Active) continue;
                        w += s.Weight;
                        // 百分比按“已用时间 ÷ 该类目上次耗时”推进，而不是按跑了几个子步骤：
                        // 子步骤数量和真实耗时不成比例（有的目录枚举一次就 10 秒）。
                        // 曲线在预计耗时处只到 95%，剩下的几个点由真实完成来补齐，收尾就不会突然跳一大截。
                        double e = (now - s.StartMs) / 1000.0;
                        if (e < 0) e = 0;
                        double exp = s.Weight;
                        if (exp < 0.4) exp = 0.4;
                        double frac;
                        if (e <= exp) frac = 0.95 * e / exp;
                        else frac = 0.95 + 0.05 * (1.0 - Math.Exp(-(e - exp) / Math.Max(3.0, exp * 0.5)));
                        f += s.Weight * frac;
                        double remain = s.Weight * (1.0 - frac);
                        if (remain > noteRemain && s.Note.Length > 0) { noteRemain = remain; noteSrc = s; }
                    }
                    if (w <= 0) return;
                    double fracTotal = f / w;
                    // 单个类目严重超时的时候，按权重算的完成度会几乎不动；再用“整体已用时间 ÷ 上次总用时”
                    // 兜一层，保证进度条始终在往前走，而不是停在某个数字上等最后一个类目
                    double globalFrac = 0;
                    if (PredTotal > 1 && T0 > 0)
                    {
                        globalFrac = (now - T0) / 1000.0 / PredTotal;
                        if (globalFrac > 0.97) globalFrac = 0.97;
                    }
                    if (globalFrac > fracTotal) fracTotal = globalFrac;
                    int pct = (int)Math.Round(Lo + (Hi - Lo) * fracTotal);
                    if (pct > Hi) pct = (int)Hi;
                    if (pct < _lastPct) pct = _lastPct;
                    if (pct == _lastPct && now - _lastMs < 120) return;
                    if (pct == _lastPct && noteSrc == null) return;
                    _lastPct = pct;
                    _lastMs = now;
                    string text = noteSrc == null || noteSrc.Note.Length == 0
                        ? Lang.T("正在扫描...")
                        : noteSrc.Note;
                    CleanEngine.Report(Prog, pct, text);
                }
            }
        }

        private static Hub _hub;

        /// <summary>上一次扫描实测耗时（秒），key 为类目名。</summary>
        private static Dictionary<string, double> _weightMap = new Dictionary<string, double>();

        private static Dictionary<string, double> ParseWeights(string text)
        {
            Dictionary<string, double> m = new Dictionary<string, double>();
            if (text == null || text.Length == 0) return m;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int c = p.LastIndexOf(':');
                if (c <= 0) continue;
                double v = 0;
                try { v = double.Parse(p.Substring(c + 1), System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception) { continue; }
                if (v > 0) m[p.Substring(0, c)] = v;
            }
            return m;
        }

        private static double WeightFor(string key, double fallback)
        {
            if (_weightMap != null && _weightMap.ContainsKey(key))
            {
                double v = _weightMap[key];
                if (v < 0.5) v = 0.5;
                if (v > 90) v = 90;
                return v;
            }
            return fallback;
        }

        /// <summary>一个类目的扫描任务；Run 为 null 表示本次设置下跳过这个类目。</summary>
        private class ScanJob
        {
            public string Key;
            public Func<CleanCategory> Run;
            public ScanJob(string key, Func<CleanCategory> run) { Key = key; Run = run; }
        }

        public static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string RoamingAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string Temp { get { return Environment.GetEnvironmentVariable("TEMP"); } }
        public static string Downloads { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); } }

        /// <summary>各类扫描耗时（诊断用，--scan 时一并写出）。</summary>
        public static List<string> Timings = new List<string>();

        /// <summary>体积统计阶段当前百分比，供大目录里的“活着”提示复用，不改变进度值。</summary>
        private static volatile int _sizePct = -1;
        private static long _sizeLastMs = 0;

        /// <summary>本次清理按扫描结果“预计要处理”的总体积，用于和实际释放量对照。</summary>
        public static long Planned = 0;

        public static List<CleanCategory> Scan(Settings s, Action<string> report)
        {
            return Scan(s, delegate (int pc, string msg) { if (report != null) report(msg); });
        }

        public static List<CleanCategory> Scan(Settings s, StepProgress prog)
        {
            List<CleanCategory> cats = new List<CleanCategory>();
            Timings.Clear();
            Report(prog, 3, Lang.T("正在列举可清理目录..."));

            // 各类目要走的根目录彼此独立，串行扫一轮（浏览器 34 s + 应用缓存 51 s + 残留 45 s）没必要排队，
            // 这里按类目并行，并行度压在 2-6，避免机械盘上开太多线程反而更慢。
            List<ScanJob> jobs = new List<ScanJob>();
            jobs.Add(new ScanJob("临时文件", delegate { return ScanUserTemp(s, null); }));
            jobs.Add(new ScanJob("浏览器缓存", delegate { return ScanBrowser(s, null); }));
            jobs.Add(new ScanJob("应用缓存", delegate { return ScanApps(s, null); }));
            jobs.Add(new ScanJob("系统痕迹", delegate { return ScanTraces(s, null); }));
            jobs.Add(new ScanJob("系统缓存与日志", delegate { return ScanSystem(s, null); }));
            jobs.Add(new ScanJob("深度清理", delegate { return ScanDeep(s, null); }));
            jobs.Add(new ScanJob("着色器", s.CleanGpuShaders ? (Func<CleanCategory>)delegate { return ScanGpu(null); } : null));
            jobs.Add(new ScanJob("日志与错误报告", delegate { return ScanLogsReports(null); }));
            jobs.Add(new ScanJob("使用痕迹", delegate { return ScanUsage(s, null); }));
            jobs.Add(new ScanJob("浏览器 Cookie", delegate { return ScanCookies(null); }));
            jobs.Add(new ScanJob("旧安装包", s.DeleteOldInstallers ? (Func<CleanCategory>)delegate { return ScanInstallers(null); } : null));
            jobs.Add(new ScanJob("大体积日志", delegate { return ScanBigLogs(null); }));
            jobs.Add(new ScanJob("回收站", delegate { return ScanRecycleBin(null); }));
            // 残留清理并进深度清理，用户只需一个清单、一次点击
            jobs.Add(new ScanJob("卸载残留", delegate { return ScanResidual(null); }));

            int dop = Math.Max(2, Math.Min(6, Environment.ProcessorCount));
            CleanCategory[] res = new CleanCategory[jobs.Count];
            string[] timeRows = new string[jobs.Count];
            int finished = 0;

            // 权重决定每个类目在进度条上占多长。上一次扫描的实测耗时优先（存在设置里），
            // 这样“浏览器 15 秒、残留 20 秒”的差异能按比例反映到进度上；没有历史就用经验值。
            double[] defaults = new double[] { 2, 7, 10, 3, 5, 3, 2, 4, 3, 2, 2, 9, 1, 10 };
            _weightMap = ParseWeights(s == null ? null : s.ScanWeights);
            Hub hub = new Hub();
            hub.Prog = prog;
            hub.Lo = 3;
            hub.Hi = 70;
            JobSlice[] slices = new JobSlice[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
            {
                if (jobs[i].Run == null) continue;
                JobSlice sl = new JobSlice();
                sl.Key = jobs[i].Key;
                sl.Weight = WeightFor(jobs[i].Key, i < defaults.Length ? defaults[i] : 3);
                // 起点先记为建表时刻：并行线程还没轮到某个类目时，它不能凭空算出一大段“已用时间”
                sl.StartMs = Environment.TickCount;
                slices[i] = sl;
                hub.Slices.Add(sl);
            }
            _hub = hub;
            int active = hub.Slices.Count;
            long hubStart = Environment.TickCount;
            hub.T0 = hubStart;
            hub.PredTotal = WeightFor("hub", 0);
            if (hub.PredTotal <= 1)
            {
                double sum = 0;
                for (int i = 0; i < hub.Slices.Count; i++) sum += hub.Slices[i].Weight;
                hub.PredTotal = sum * 1.15;
            }
            SliceNote(Lang.T("正在列举可清理目录..."));

            // 心跳：有的类目一次目录枚举要十几秒、期间没有任何回调，只靠子步骤上报就会停住；
            // 每 200 ms 重算一次进度，保证“目录在变、进度也在走”
            System.Threading.Timer heartbeat = new System.Threading.Timer(delegate
            {
                Hub h = _hub;
                if (h != null) h.Report();
            }, null, 120, 200);

            System.Threading.Tasks.Parallel.For(0, jobs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = dop },
                delegate (int idx)
                {
                    ScanJob job = jobs[idx];
                    JobSlice sl = slices[idx];
                    if (job.Run == null || sl == null) return;
                    _slice = sl;
                    sl.StartMs = Environment.TickCount;
                    DateTime t0 = DateTime.Now;
                    CleanCategory got = null;
                    try { got = job.Run(); }
                    catch (Exception) { got = null; }
                    if (got == null) got = new CleanCategory();
                    res[idx] = got;
                    timeRows[idx] = job.Key + "\t" + ((int)(DateTime.Now - t0).TotalMilliseconds) + " ms\t" + got.Items.Count;
                    sl.Note = job.Key + Lang.T(" · 扫描完成");
                    sl.Finished = true;
                    _slice = null;
                    int done = System.Threading.Interlocked.Increment(ref finished);
                    hub.Report();
                });
            _hub = null;
            if (heartbeat != null) heartbeat.Dispose();
            long hubMs = Environment.TickCount - hubStart;
            hub.Slices.Clear(); // 类目都跑完了，切片不再参与合成

            for (int i = 0; i < jobs.Count; i++)
            {
                if (res[i] != null) cats.Add(res[i]);
                if (timeRows[i] != null) Timings.Add(timeRows[i]);
            }

            // 把这次各类目的真实耗时记进设置，下一次扫描按它分配进度段：
            // 慢类目分到更长的段，进度条就不会长时间钉在一个数字上
            string weightText = "";
            if (timeRows != null)
            {
                for (int i = 0; i < timeRows.Length; i++)
                {
                    if (timeRows[i] == null) continue;
                    string[] p = timeRows[i].Split('\t');
                    if (p.Length < 2) continue;
                    double ms = 0;
                    try { ms = double.Parse(p[1].Replace(" ms", "").Trim(), System.Globalization.CultureInfo.InvariantCulture); }
                    catch (Exception) { continue; }
                    if (ms < 300) ms = 300;
                    if (weightText.Length > 0) weightText += ",";
                    weightText += p[0] + ":" + (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            // 发现阶段用掉的时间也要能对上整体节奏，体积统计阶段的预计耗时单独记
            double hubSec = Math.Max(0.3, hubMs / 1000.0);
            if (weightText.Length > 0) weightText += ",";
            weightText += "hub" + ":" + hubSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            List<CleanItem> all = new List<CleanItem>();
            for (int i = 0; i < cats.Count; i++)
            {
                for (int j = 0; j < cats[i].Items.Count; j++)
                {
                    CleanItem it = cats[i].Items[j];
                    if (it.Kind == CleanKind.RecycleBin || it.Kind == CleanKind.OrphanGroup) continue;
                    all.Add(it);
                }
            }

            if (all.Count > 0)
            {
                Report(prog, 70, Lang.T("正在统计 ") + all.Count + Lang.T(" 个项目的体积..."));
                int done = 0;
                int lastPct = 70;
                _sizePct = 70;
                _sizeLastMs = 0;
                long sizeStart = Environment.TickCount;
                // 体积统计的耗时和项目数不成正比（少数几 GB 的目录占掉大半时间），
                // 所以进度同时看“已统计项数”和“已用时间 / 上次用时”，谁快跟谁，避免项目数扫完就冲到 96
                double predSize = WeightFor(Lang.T("体积统计"), 0);
                FileUtil.SizeHook = delegate (long bytes)
                {
                    long now = Environment.TickCount;
                    if (now - _sizeLastMs < 300) return;
                    _sizeLastMs = now;
                    Report(prog, _sizePct, Lang.T("统计体积（") + Byt.Format(bytes) + Lang.T("，大目录逐个文件统计中）"));
                };
                System.Threading.Tasks.Parallel.For(0, all.Count, delegate (int idx)
                {
                    CleanItem it = all[idx];
                    try { SizeItem(it); }
                    catch (Exception) { }
                    int d = System.Threading.Interlocked.Increment(ref done);
                    double itemFrac = (double)d / all.Count;
                    if (itemFrac > 1.0) itemFrac = 1.0;
                    double timeFrac = 0;
                    // 体积统计的耗时集中在少数几个大目录上：按项目数算会几秒冲到 90% 再干等，
                    // 所以有历史用时之后以“已用时间 / 上次用时”为主，项目数只用来给文案
                    if (predSize > 0.3) timeFrac = (Environment.TickCount - sizeStart) / 1000.0 / predSize;
                    if (timeFrac > 0) itemFrac = timeFrac;
                    if (itemFrac > 0.97) itemFrac = 0.97;
                    int pct = 70 + (int)(26 * itemFrac);
                    if (pct > lastPct)
                    {
                        System.Threading.Interlocked.Exchange(ref lastPct, pct);
                        _sizePct = pct;
                        string label = it.Name;
                        if (label.Length > 26) label = label.Substring(0, 26) + "…";
                        Report(prog, pct, Lang.T("统计体积 ") + d + "/" + all.Count + " · " + label);
                    }
                });
                FileUtil.SizeHook = null;
                double sizeSec = Math.Max(0.3, (Environment.TickCount - sizeStart) / 1000.0);
                if (weightText.Length > 0) weightText += ",";
                weightText += "sizing" + ":" + sizeSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (s != null && weightText.Length > 0)
            {
                s.ScanWeights = weightText;
                try { s.Save(); } catch (Exception) { }
            }

            // 去掉体积为 0 的空项
            for (int i = 0; i < cats.Count; i++)
            {
                List<CleanItem> keep = new List<CleanItem>();
                for (int j = 0; j < cats[i].Items.Count; j++)
                {
                    if (cats[i].Items[j].Bytes > 0) keep.Add(cats[i].Items[j]);
                }
                cats[i].Items = keep;
            }
            // 设置里关掉“移入回收站”后，所有条目一律永久删除：确认框与实际行为必须一致
            if (!s.UseRecycleBin)
            {
                for (int i = 0; i < cats.Count; i++)
                {
                    for (int j = 0; j < cats[i].Items.Count; j++) cats[i].Items[j].Recycle = false;
                }
            }
            Report(prog, 97, Lang.T("整理清单..."));
            Report(prog, 100, Lang.T("扫描完成"));
            return cats;
        }

        private static void R(Action<string> report, string msg) { if (report != null) report(msg); }

        private static CleanCategory NewCat(string id, string name, string desc, string glyph, bool on)
        {
            CleanCategory c = new CleanCategory();
            c.Id = id; c.Name = name; c.Desc = desc; c.Glyph = glyph; c.DefaultOn = on;
            return c;
        }

        private static void AddDirItem(CleanCategory cat, string name, string path, CleanKind kind, int days, bool recycle)
        {
            AddDirItem(cat, name, path, kind, days, recycle, false);
        }

        private static void AddDirItem(CleanCategory cat, string name, string path, CleanKind kind, int days, bool recycle, bool optIn)
        {
            AddDirItem(cat, name, path, kind, days, recycle, optIn, false);
        }

        private static void AddDirItem(CleanCategory cat, string name, string path, CleanKind kind, int days, bool recycle, bool optIn, bool defaultOff)
        {
            if (path == null || path.Length == 0) return;
            if (!Directory.Exists(path)) return;
            if (!Guard.CanDelete(path, true))
            {
                return;
            }
            string key = Guard.Norm(path);
            for (int i = 0; i < cat.Items.Count; i++)
            {
                if (Guard.Norm(cat.Items[i].Path) == key) return;
            }
            CleanItem it = new CleanItem();
            it.CategoryId = cat.Id;
            string n2 = name == null ? "" : name.Trim();
            if (n2.EndsWith("·")) n2 = n2.TrimEnd('·').Trim();
            it.Name = n2;
            it.Path = path;
            it.Kind = kind;
            it.OlderThanDays = days;
            it.Recycle = recycle;
            it.OptIn = optIn;
            it.DefaultOff = defaultOff;
            it.HighRisk = optIn; // 需手动开启的目录项都属于“删了没有退路”，界面标红
            it.Exists = true;
            it.Bytes = 0;
            it.Files = 0;
            cat.Items.Add(it);
        }

        /// <summary>单项体积计算（可并行调用）。</summary>
        public static void SizeItem(CleanItem it)
        {
            long total = 0;
            int files = 0;
            if (it.Kind == CleanKind.OldFilesInDir && it.OlderThanDays > 0)
            {
                FileUtil.SizeOfOld(it.Path, DateTime.Now.AddDays(-it.OlderThanDays), ref total, ref files, 0);
            }
            else if (it.Kind == CleanKind.DirContents)
            {
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(it.Path); }
                catch (Exception) { return; }
                for (int i = 0; i < entries.Length; i++)
                {
                    if (Guard.CanDelete(entries[i], true)) FileUtil.SizeOf(entries[i], ref total, ref files, 0);
                }
            }
            else if (it.Kind == CleanKind.OldFilesExt)
            {
                if (total == 0) return; // 已由扫描阶段统计
            }
            else if (it.Kind == CleanKind.FileItself)
            {
                try { total = new FileInfo(it.Path).Length; files = 1; } catch (Exception) { }
            }
            else
            {
                FileUtil.SizeOf(it.Path, ref total, ref files, 0);
            }
            it.Bytes = total;
            it.Files = files;
        }

        private static readonly string[] SkipAppNames = new string[]
        {
            "microsoft", "windows", "temp", "packages", "programs", "programsdata", "intel", "amd", "nvidia", "google", "mozilla", "adobe"
        };

        /// <summary>自动发现其他 Chromium 内核浏览器的 User Data 目录。</summary>
        private static void ScanChromiumGeneric(CleanCategory cat, Settings s)
        {
            string[] roots = new string[] { LocalAppData, RoamingAppData };
            string[] rootLeaves = new string[] { "ShaderCache", "GrShaderCache", "Crashpad", "BrowserMetrics" };
            for (int r = 0; r < roots.Length; r++)
            {
                string[] dirs;
                try { dirs = Directory.GetDirectories(roots[r]); }
                catch (Exception) { continue; }
                for (int i = 0; i < dirs.Length && cat.Items.Count < 400; i++)
                {
                    SliceTick(Lang.T("检查浏览器目录 · ") + Path.GetFileName(dirs[i]));
                    string userData = Path.Combine(dirs[i], "User Data");
                    if (!Directory.Exists(Path.Combine(userData, "Default"))) continue;
                    if (!Directory.Exists(Path.Combine(userData, @"Default\Cache"))) continue;
                    string label = Path.GetFileName(dirs[i]);
                    if (DupLabel(cat, label)) continue;
                    ScanChromium(cat, s, label, userData, rootLeaves);
                }
            }
        }

        private static bool DupLabel(CleanCategory cat, string label)
        {
            for (int i = 0; i < cat.Items.Count; i++)
            {
                if (cat.Items[i].Name.StartsWith(label + " · ")) return true;
            }
            return false;
        }

        /// <summary>
        /// 自动发现嵌套的 Web 缓存：&lt;app&gt;\User Data\&lt;profile&gt;\Cache、
        /// &lt;app&gt;\Partitions\&lt;part&gt;\Cache、&lt;app&gt;\browser-data\Default\Cache、
        /// &lt;app&gt;\cache.x64.*\... 这类目录（Electron / CEF / 微信开发者工具等常见布局）。
        /// </summary>
        private static void ScanNestedWebCaches(CleanCategory cat, Settings s)
        {
            string[] roots = new string[] { LocalAppData, RoamingAppData, Path.Combine(LocalAppData, "Programs") };
            string[] leaves = new string[] { "Cache", "Code Cache", "GPUCache" };
            for (int r = 0; r < roots.Length; r++)
            {
                string[] apps;
                try { apps = Directory.GetDirectories(roots[r]); }
                catch (Exception) { continue; }
                for (int i = 0; i < apps.Length && cat.Items.Count < 1200; i++)
                {
                    string app = apps[i];
                    string appName = Path.GetFileName(app);
                    SliceTick(Lang.T("检查缓存目录 · ") + appName);
                    string low = appName.ToLowerInvariant();
                    if (low == "microsoft" || low == "windows" || low == "packages" || low == "temp" || low == "programs") continue;
                    if (DupLabel(cat, appName)) continue;
                    ScanNestedApp(cat, s, appName, app, leaves);
                    string[] subs;
                    try { subs = Directory.GetDirectories(app); }
                    catch (Exception) { continue; }
                    for (int j = 0; j < subs.Length && cat.Items.Count < 1200; j++)
                    {
                        string subName = Path.GetFileName(subs[j]);
                        string subLow = subName.ToLowerInvariant();
                        if (subLow == "user data" || subLow == "partitions" || subLow == "browser-data") continue;
                        ScanNestedApp(cat, s, appName + " · " + subName, subs[j], leaves);
                    }
                    // User Data / browser-data 下的 profile 与 Partitions 单独展开
                    ScanNestedProfileRoot(cat, s, appName, Path.Combine(app, "User Data"), leaves);
                    ScanNestedProfileRoot(cat, s, appName, Path.Combine(app, "browser-data"), leaves);
                    ScanPartitions(cat, s, appName, Path.Combine(app, "Partitions"));
                }
            }
        }

        /// <summary>app 目录本身或子目录如果是 profile 容器，就把里面的 profile 摊开。</summary>
        private static void ScanNestedApp(CleanCategory cat, Settings s, string label, string dir, string[] leaves)
        {
            bool isProfile = Directory.Exists(Path.Combine(dir, "Default")) || Directory.Exists(Path.Combine(dir, "Profile 1"));
            if (isProfile)
            {
                ScanProfiles(cat, s, label, dir, leaves);
                return;
            }
            for (int i = 0; i < leaves.Length; i++)
            {
                AddDirItem(cat, label + " · " + leaves[i], Path.Combine(dir, leaves[i]), CleanKind.DirContents, 0, s.UseRecycleBin);
            }
        }

        private static void ScanNestedProfileRoot(CleanCategory cat, Settings s, string label, string root, string[] leaves)
        {
            if (!Directory.Exists(root)) return;
            ScanProfiles(cat, s, label, root, leaves);
        }

        private static void ScanProfiles(CleanCategory cat, Settings s, string label, string root, string[] leaves)
        {
            string[] profiles;
            try { profiles = Directory.GetDirectories(root); }
            catch (Exception) { return; }
            for (int i = 0; i < profiles.Length && cat.Items.Count < 1200; i++)
            {
                string prof = profiles[i];
                string profName = Path.GetFileName(prof);
                string pl = profName.ToLowerInvariant();
                if (pl != "default" && !pl.StartsWith("profile") && !pl.StartsWith("guest")) continue;
                for (int j = 0; j < leaves.Length; j++)
                {
                    AddDirItem(cat, label + " · " + profName + "\\" + leaves[j], Path.Combine(prof, leaves[j]), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
        }

        private static void ScanPartitions(CleanCategory cat, Settings s, string label, string root)
        {
            if (!Directory.Exists(root)) return;
            string[] parts;
            try { parts = Directory.GetDirectories(root); }
            catch (Exception) { return; }
            string[] leaves = new string[] { "Cache", "Code Cache", "GPUCache" };
            for (int i = 0; i < parts.Length && cat.Items.Count < 1200; i++)
            {
                string pname = Path.GetFileName(parts[i]);
                for (int j = 0; j < leaves.Length; j++)
                {
                    AddDirItem(cat, label + " · Partitions\\" + pname + "\\" + leaves[j], Path.Combine(parts[i], leaves[j]), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
        }

        /// <summary>自动发现 Electron 类应用的标准缓存目录（Cache / Code Cache / GPUCache 等）。</summary>
        private static void ScanElectronGeneric(CleanCategory cat, Settings s)
        {
            string[] roots = new string[] { RoamingAppData, LocalAppData, Path.Combine(LocalAppData, "Programs") };
            string[] leaves = new string[] { "Cache", "Code Cache", "GPUCache", "ShaderCache", "DawnGraphiteCache", "DawnWebGPUCache" };
            for (int r = 0; r < roots.Length; r++)
            {
                string[] dirs;
                try { dirs = Directory.GetDirectories(roots[r]); }
                catch (Exception) { continue; }
                for (int i = 0; i < dirs.Length && cat.Items.Count < 900; i++)
                {
                    string dir = dirs[i];
                    string name = Path.GetFileName(dir);
                    SliceTick(Lang.T("检查应用缓存 · ") + name);
                    string low = name.ToLowerInvariant();
                    bool skip = false;
                    for (int k = 0; k < SkipAppNames.Length; k++) { if (low == SkipAppNames[k]) { skip = true; break; } }
                    if (skip) continue;
                    for (int j = 0; j < leaves.Length; j++)
                    {
                        string p = Path.Combine(dir, leaves[j]);
                        if (!Directory.Exists(p)) continue;
                        AddDirItem(cat, name + " · " + leaves[j], p, CleanKind.DirContents, 0, s.UseRecycleBin);
                    }
                }
            }
        }

        private static CleanCategory ScanUserTemp(Settings s, Action<string> report)
        {
            string desc = s.TempDays <= 0
                ? Lang.T("系统与用户临时目录里未被占用的全部文件")
                : Lang.T("系统与用户临时目录中 ") + s.TempDays + Lang.T(" 天前的文件");
            CleanCategory cat = NewCat("temp", Lang.T("临时文件"), desc, "🕒", true);
            R(report, Lang.T("扫描临时文件..."));
            string tn = s.TempDays <= 0 ? Lang.T("用户临时目录") : Lang.T("用户临时目录（") + s.TempDays + Lang.T(" 天前）");
            AddDirItem(cat, tn, Temp, CleanKind.OldFilesInDir, s.TempDays, s.UseRecycleBin);
            string localTemp = Path.Combine(LocalAppData, "Temp");
            if (Guard.Norm(localTemp) != Guard.Norm(Temp))
            {
                AddDirItem(cat, Lang.T("本地临时目录"), localTemp, CleanKind.OldFilesInDir, s.TempDays, s.UseRecycleBin);
            }
            return cat;
        }

        private static string[] ChromiumLeaves()
        {
            // DawnCache / Media Cache / Shared Dictionary\cache 来自 MangoDisk 与 Mole 的规则集：
            // 都是可重建的渲染缓存，不涉及登录态、书签、扩展本体
            return new string[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
                                  "Media Cache", @"Service Worker\ScriptCache", @"Shared Dictionary\cache" };
        }

        private static void ScanChromium(CleanCategory cat, Settings s, string label, string root, string[] rootLeaves)
        {
            if (!Directory.Exists(root)) return;
            string[] leaves = ChromiumLeaves();
            string[] profiles;
            try { profiles = Directory.GetDirectories(root); }
            catch (Exception) { profiles = new string[0]; }
            for (int i = 0; i < profiles.Length; i++)
            {
                string prof = profiles[i];
                string profName = Path.GetFileName(prof);
                if (!profName.StartsWith("Default") && !profName.StartsWith("Profile") && !profName.StartsWith("Guest")) continue;
                for (int j = 0; j < leaves.Length; j++)
                {
                    AddDirItem(cat, label + " · " + profName + "\\" + leaves[j], Path.Combine(prof, leaves[j]), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
            if (rootLeaves != null)
            {
                for (int j = 0; j < rootLeaves.Length; j++)
                {
                    AddDirItem(cat, label + " · " + rootLeaves[j], Path.Combine(root, rootLeaves[j]), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
        }

        private static CleanCategory ScanBrowser(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("browser", Lang.T("浏览器缓存"), Lang.T("网页缓存、着色器缓存与代码缓存，不影响书签与登录状态"), "🌐", true);
            R(report, Lang.T("扫描浏览器缓存..."));
            string la = LocalAppData;

            string[] rootLeaves = new string[] { "ShaderCache", "GrShaderCache", "Crashpad", "BrowserMetrics",
                                                 "GraphiteDawnCache", "GPUPersistentCache", "component_crx_cache", "extensions_crx_cache" };
            ScanChromium(cat, s, "Chrome", Path.Combine(la, @"Google\Chrome\User Data"), rootLeaves);
            ScanChromium(cat, s, "Chrome Beta", Path.Combine(la, @"Google\Chrome Beta\User Data"), rootLeaves);
            ScanChromium(cat, s, "Chrome Canary", Path.Combine(la, @"Google\Chrome SxS\User Data"), rootLeaves);
            ScanChromium(cat, s, "Edge", Path.Combine(la, @"Microsoft\Edge\User Data"), new string[] { "ShaderCache" });
            ScanChromium(cat, s, "Edge Dev", Path.Combine(la, @"Microsoft\Edge Dev\User Data"), rootLeaves);
            ScanChromium(cat, s, "Brave", Path.Combine(la, @"BraveSoftware\Brave-Browser\User Data"), null);
            ScanChromium(cat, s, "Vivaldi", Path.Combine(la, @"Vivaldi\User Data"), null);
            ScanChromium(cat, s, Lang.T("360 极速浏览器"), Path.Combine(la, @"360Chrome\Chrome\User Data"), rootLeaves);
            ScanChromium(cat, s, Lang.T("夸克浏览器"), Path.Combine(la, @"Quark\User Data"), rootLeaves);
            ScanChromium(cat, s, "Opera", Path.Combine(la, @"Opera Software\Opera Stable"), null);
            ScanChromium(cat, s, "Opera GX", Path.Combine(la, @"Opera Software\Opera GX Stable"), null);
            ScanChromium(cat, s, "Yandex", Path.Combine(la, @"Yandex\YandexBrowser\User Data"), rootLeaves);
            ScanChromium(cat, s, Lang.T("搜狗浏览器"), Path.Combine(la, @"SogouExplorer\User Data"), rootLeaves);
            ScanChromium(cat, s, Lang.T("QQ 浏览器"), Path.Combine(la, @"Tencent\QQBrowser\User Data"), rootLeaves);
            ScanChromium(cat, s, Lang.T("2345 浏览器"), Path.Combine(la, @"2345Soft\2345Explorer\User Data"), rootLeaves);
            ScanChromium(cat, s, Lang.T("猎豹浏览器"), Path.Combine(la, @"Kingsoft\LieBao\User Data"), rootLeaves);
            ScanChromiumGeneric(cat, s);
            ScanNestedWebCaches(cat, s);

            // WebView2（Edge 内核嵌入控件）缓存
            string[] wvRoots = new string[]
            {
                Path.Combine(la, @"Microsoft\EdgeWebView"),
                Path.Combine(la, @"Microsoft\EdgeWebView\EBWebView"),
                Path.Combine(la, @"Microsoft\EdgeUpdate")
            };
            for (int i = 0; i < wvRoots.Length; i++)
            {
                AddDirItem(cat, "WebView2 · Default\\Cache", Path.Combine(wvRoots[i], @"Default\Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
                AddDirItem(cat, "WebView2 · Default\\Code Cache", Path.Combine(wvRoots[i], @"Default\Code Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
                AddDirItem(cat, "WebView2 · EBWebView\\Default\\Cache", Path.Combine(wvRoots[i], @"EBWebView\Default\Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            }

            // Firefox：本地缓存目录 + 启动缓存
            string ffLocal = Path.Combine(la, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffLocal))
            {
                string[] profiles;
                try { profiles = Directory.GetDirectories(ffLocal); }
                catch (Exception) { profiles = new string[0]; }
                for (int i = 0; i < profiles.Length; i++)
                {
                    AddDirItem(cat, "Firefox · " + Path.GetFileName(profiles[i]) + "\\cache2", Path.Combine(profiles[i], "cache2"), CleanKind.DirContents, 0, s.UseRecycleBin);
                    AddDirItem(cat, Lang.T("Firefox · 启动缓存"), Path.Combine(profiles[i], "startupCache"), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
            return cat;
        }

        private static void AddAppLeaves(CleanCategory cat, Settings s, string label, string root, string[] leaves)
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                string label2 = leaves[i].Length > 0 ? label + " · " + leaves[i] : label;
                AddDirItem(cat, label2, Path.Combine(root, leaves[i]), CleanKind.DirContents, 0, s.UseRecycleBin);
            }
        }

        private static CleanCategory ScanApps(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("apps", Lang.T("应用缓存"), Lang.T("聊天、办公、剪辑类应用的缓存，保留账号与本地文件"), "📦", true);
            R(report, Lang.T("扫描应用缓存..."));
            string ra = RoamingAppData;
            string la = LocalAppData;
            string[] electron = new string[] { "Cache", "Code Cache", "GPUCache" };
            string[] electronFull = new string[] { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache" };

            AddAppLeaves(cat, s, "Discord", Path.Combine(ra, "discord"), electron);
            AddAppLeaves(cat, s, "Slack", Path.Combine(ra, "Slack"), electron);
            AddAppLeaves(cat, s, "Teams", Path.Combine(ra, @"Microsoft\Teams"), new string[] { "Cache", "GPUCache" });
            AddAppLeaves(cat, s, "VS Code", Path.Combine(ra, "Code"), new string[] { "Cache", "CachedData", "Code Cache", "GPUCache" });
            AddAppLeaves(cat, s, "Cursor", Path.Combine(ra, "Cursor"), electron);
            AddAppLeaves(cat, s, "Trae", Path.Combine(ra, "Trae"), electronFull);
            AddAppLeaves(cat, s, "Obsidian", Path.Combine(ra, "Obsidian"), electron);
            AddAppLeaves(cat, s, "Notion", Path.Combine(ra, "Notion"), electron);
            AddAppLeaves(cat, s, "QQ", Path.Combine(ra, "QQ"), new string[] { "Cache", "Code Cache" });
            AddAppLeaves(cat, s, Lang.T("飞书 Feishu"), Path.Combine(ra, "LarkShell"), new string[] { @"aha\users\global\profile_global\Cache", @"aha\users\global\profile_global\Code Cache", @"aha\users\global\profile_global\GPUCache", "ShaderCache", "GrShaderCache" });
            AddAppLeaves(cat, s, "Lark", Path.Combine(ra, "LarkInternational"), new string[] { @"aha\users\global\profile_global\Cache", @"aha\users\global\profile_global\Code Cache", @"aha\users\global\profile_global\GPUCache", "ShaderCache", "GrShaderCache" });
            AddAppLeaves(cat, s, Lang.T("剪映 JianyingPro"), Path.Combine(la, @"JianyingPro\User Data\CEF"), new string[] { @"Cache\Cache", @"Cache\Code Cache", @"Cache\GPUCache", "ShaderCache", "GrShaderCache" });
            AddAppLeaves(cat, s, "CapCut", Path.Combine(la, @"CapCut\User Data\CEF"), new string[] { @"Cache\Cache", @"Cache\Code Cache", @"Cache\GPUCache", "ShaderCache" });
            AddAppLeaves(cat, s, "Epic Games", Path.Combine(la, @"EpicGamesLauncher\Saved\webcache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("WPS Office 缓存"), Path.Combine(ra, @"Kingsoft\office6\cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Office 文件缓存"), Path.Combine(la, @"Microsoft\Office\16.0\OfficeFileCache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Office 未保存文件"), Path.Combine(la, @"Microsoft\Office\UnsavedFiles"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Adobe 媒体缓存"), Path.Combine(ra, @"Adobe\Common\Media Cache Files"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Adobe 媒体缓存数据库"), Path.Combine(ra, @"Adobe\Common\Media Cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Steam 网页缓存"), Path.Combine(la, @"Steam\htmlcache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("Steam 着色器缓存"), Path.Combine(la, @"Steam\steamapps\shadercache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("网易云音乐缓存"), Path.Combine(la, @"Netease\CloudMusic\Cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("企业微信缓存"), Path.Combine(ra, @"Tencent\WXWork\Global\Cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("微信开发者工具缓存"), Path.Combine(la, @"微信开发者工具\User Data"), new string[] { @"Default\Cache", @"Default\Code Cache", @"Default\GPUCache" });
            AddAppLeaves(cat, s, Lang.T("钉钉缓存"), Path.Combine(la, @"DingTalk\cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("腾讯会议缓存"), Path.Combine(la, @"Tencent\WeMeet\Cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("QQ音乐缓存"), Path.Combine(la, @"Tencent\QQMusic\Cache"), new string[] { "" });
            AddAppLeaves(cat, s, Lang.T("百度网盘缓存"), Path.Combine(la, @"baidu\BaiduNetdisk\Cache"), new string[] { "" });
            // 自动发现其余 Electron 应用（VS Code 类、Slack 类、各类客户端）的标准缓存目录
            ScanElectronGeneric(cat, s);
            // 应用日志、更新包下载目录、npm 缓存：360 那类工具算的“常用软件垃圾”主要就是这些
            ScanAppLogsAndUpdates(cat, s);
            // 以下两组规则取自 MangoDisk / Mole 的规则集：开发缓存与微信 Chromium 渲染缓存，都是可重建产物
            ScanDevCaches(cat, s);
            ScanWeChatCaches(cat, s);

            // UWP / Microsoft Store 应用缓存
            string packages = Path.Combine(la, "Packages");
            if (Directory.Exists(packages))
            {
                string[] pkgs;
                try { pkgs = Directory.GetDirectories(packages); }
                catch (Exception) { pkgs = new string[0]; }
                for (int i = 0; i < pkgs.Length; i++)
                {
                    string pkg = pkgs[i];
                    string name = Path.GetFileName(pkg);
                    AddDirItem(cat, "UWP · " + name + "\\LocalCache", Path.Combine(pkg, "LocalCache"), CleanKind.DirContents, 0, s.UseRecycleBin);
                    AddDirItem(cat, "UWP · " + name + "\\TempState", Path.Combine(pkg, "TempState"), CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
            return cat;
        }

        private static CleanCategory ScanGpu(Action<string> report)
        {
            CleanCategory cat = NewCat("gpu", Lang.T("GPU 着色器缓存"), Lang.T("显卡编译缓存，删除后游戏与应用首次启动会重新编译"), "🎮", false);
            R(report, Lang.T("扫描着色器缓存..."));
            string la = LocalAppData;
            string ra = RoamingAppData;
            AddDirItem(cat, "NVIDIA DXCache", Path.Combine(la, @"NVIDIA\DXCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, "NVIDIA GLCache", Path.Combine(la, @"NVIDIA\GLCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, "AMD DXCache", Path.Combine(la, @"AMD\DXCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, "AMD VkCache", Path.Combine(la, @"AMD\VkCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, "Intel ShaderCache", Path.Combine(la, @"Intel\ShaderCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, Lang.T("DirectX 着色器缓存"), Path.Combine(la, @"D3DSCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, Lang.T("DirectX 管道缓存"), Path.Combine(la, @"Microsoft\DirectX Shader Cache"), CleanKind.DirContents, 0, false);
            return cat;
        }

        private static CleanCategory ScanLogsReports(Action<string> report)
        {
            CleanCategory cat = NewCat("logs", Lang.T("日志与错误报告"), Lang.T("崩溃转储、错误报告与 7 天前的应用日志"), "🧾", true);
            R(report, Lang.T("扫描日志与崩溃报告..."));
            string la = LocalAppData;
            string ra = RoamingAppData;
            AddDirItem(cat, Lang.T("Windows 错误报告归档"), Path.Combine(la, @"Microsoft\Windows\WER\ReportArchive"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("Windows 错误报告队列"), Path.Combine(la, @"Microsoft\Windows\WER\ReportQueue"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("崩溃转储 CrashDumps"), Path.Combine(la, "CrashDumps"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("错误报告归档（系统级）"), @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("错误报告队列（系统级）"), @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("WER 临时报告"), @"C:\ProgramData\Microsoft\Windows\WER\Temp", CleanKind.DirContents, 0, true);
            AddFileItem(cat, Lang.T("完整内存转储 MEMORY.DMP"), @"C:\Windows\MEMORY.DMP", false);
            AddDirItem(cat, Lang.T("Office 文件缓存"), Path.Combine(la, @"Microsoft\Office\16.0\OfficeFileCache"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("IE / INetCache 临时文件"), Path.Combine(la, @"Microsoft\Windows\INetCache"), CleanKind.DirContents, 0, true);
            // Windows 磁盘清理里的“已下载的程序文件”，平时基本是空的，留着对齐口径
            AddDirItem(cat, Lang.T("已下载的程序文件"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Downloaded Program Files"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("Adobe Reader 缓存"), Path.Combine(la, @"Adobe\Acrobat\DC\Cache"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("WPS 缓存"), Path.Combine(ra, @"Kingsoft\office6\cache"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("腾讯会议日志"), Path.Combine(la, @"Tencent\Wemeet\logs"), CleanKind.OldFilesInDir, 7, true);
            AddDirItem(cat, Lang.T("百度网盘日志"), Path.Combine(ra, @"BaiduYunGuanjia\logs"), CleanKind.OldFilesInDir, 7, true);
            AddDirItem(cat, Lang.T("Docker 日志"), Path.Combine(la, @"Docker\log"), CleanKind.OldFilesInDir, 7, true);
            AddDirItem(cat, Lang.T("WPS 日志"), Path.Combine(ra, @"Kingsoft\office6\log"), CleanKind.OldFilesInDir, 7, true);
            return cat;
        }

        /// <summary>
        /// 应用日志（sdk_storage\log、&lt;app&gt;\log）、更新包下载目录（update_download / update）、npm 缓存。
        /// 这些是 360 等工具“常用软件垃圾”里的大头，且都能自动重建，不涉及任何用户数据。
        /// </summary>
        private static void ScanAppLogsAndUpdates(CleanCategory cat, Settings s)
        {
            string la = LocalAppData;
            string ra = RoamingAppData;

            AddDirItem(cat, "npm 缓存", Path.Combine(la, "npm-cache"), CleanKind.DirContents, 0, s.UseRecycleBin);

            string[] roots = new string[] { la, ra };
            string[] logNames = new string[] { "log", "logs", "Log", "Logs" };
            for (int r = 0; r < roots.Length; r++)
            {
                string[] apps;
                try { apps = Directory.GetDirectories(roots[r]); }
                catch (Exception) { continue; }
                for (int i = 0; i < apps.Length; i++)
                {
                    string app = apps[i];
                    string name = Path.GetFileName(app);
                    try
                    {
                        for (int j = 0; j < logNames.Length; j++)
                        {
                            string p = Path.Combine(app, logNames[j]);
                            if (!Directory.Exists(p)) continue;
                            AddDirItem(cat, name + " · " + logNames[j], p, CleanKind.DirContents, 0, s.UseRecycleBin);
                        }
                        // Electron/CEF 客户端把日志放在 User Data\<profile>\log 或 sdk_storage\log
                        string ud = Path.Combine(app, "User Data");
                        if (Directory.Exists(ud))
                        {
                            string sd = Path.Combine(ud, "sdk_storage");
                            if (Directory.Exists(sd))
                            {
                                string sl = Path.Combine(sd, "log");
                                if (Directory.Exists(sl)) AddDirItem(cat, name + " · sdk_storage\\log", sl, CleanKind.DirContents, 0, s.UseRecycleBin);
                            }
                            string[] profs;
                            try { profs = Directory.GetDirectories(ud); }
                            catch (Exception) { profs = new string[0]; }
                            for (int k = 0; k < profs.Length; k++)
                            {
                                string prof = Path.GetFileName(profs[k]);
                                for (int j = 0; j < logNames.Length; j++)
                                {
                                    string p = Path.Combine(profs[k], logNames[j]);
                                    if (!Directory.Exists(p)) continue;
                                    AddDirItem(cat, name + " · " + prof + "\\" + logNames[j], p, CleanKind.DirContents, 0, s.UseRecycleBin);
                                }
                            }
                        }
                        // 更新包下载目录：里面是已经下完的安装包，删了下次更新会重下
                        string[] upds;
                        try { upds = Directory.GetDirectories(app, "update*"); }
                        catch (Exception) { upds = new string[0]; }
                        for (int u = 0; u < upds.Length; u++)
                        {
                            AddDirItem(cat, name + " · " + Path.GetFileName(upds[u]), upds[u], CleanKind.DirContents, 0, s.UseRecycleBin);
                        }
                        // gecko / cef 缓存
                        foreach (string leaf in new string[] { "gecko_cache", "cef_cache", "htmlcache" })
                        {
                            string p = Path.Combine(app, leaf);
                            if (Directory.Exists(p)) AddDirItem(cat, name + " · " + leaf, p, CleanKind.DirContents, 0, s.UseRecycleBin);
                        }
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// 开发工具的包与编译缓存。规则来自 MangoDisk / Mole 的 dev 类目：
        /// 都是「删了会重新下载或重新编译、但不丢源码与配置」的可重建产物。
        /// 重建代价高的（cargo / go / gradle / ccache / sccache）默认不勾，但不算高风险。
        /// </summary>
        private static void ScanDevCaches(CleanCategory cat, Settings s)
        {
            string la = LocalAppData;
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AddDirItem(cat, "pnpm · 包存储", Path.Combine(la, @"pnpm\store"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "pnpm · 旧缓存", Path.Combine(la, "pnpm-cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "Yarn · 缓存", Path.Combine(la, @"Yarn\Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "Yarn Berry · 缓存", Path.Combine(la, @"Yarn\Berry\cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "pip · 包缓存", Path.Combine(la, @"pip\Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "uv · 缓存", Path.Combine(la, @"uv\cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "NuGet · 包缓存", Path.Combine(la, @"NuGet\v3-cache"), CleanKind.DirContents, 0, s.UseRecycleBin);
            AddDirItem(cat, "NuGet · 旧缓存", Path.Combine(la, @"NuGet\Cache"), CleanKind.DirContents, 0, s.UseRecycleBin);

            AddDirItem(cat, "Cargo · 注册表包缓存", Path.Combine(up, @".cargo\registry\cache"), CleanKind.DirContents, 0, s.UseRecycleBin, false, true);
            AddDirItem(cat, "Go · 构建缓存", Path.Combine(la, "go-build"), CleanKind.DirContents, 0, s.UseRecycleBin, false, true);
            AddDirItem(cat, "Gradle · 依赖缓存", Path.Combine(up, @".gradle\caches"), CleanKind.DirContents, 0, s.UseRecycleBin, false, true);
            AddDirItem(cat, "ccache · 编译缓存", Path.Combine(la, "ccache"), CleanKind.DirContents, 0, s.UseRecycleBin, false, true);
            AddDirItem(cat, "sccache · 编译缓存", Path.Combine(la, @"Mozilla\sccache"), CleanKind.DirContents, 0, s.UseRecycleBin, false, true);
        }

        /// <summary>
        /// 微信 / Weixin 的 Chromium 渲染缓存（规则取自 MangoDisk 的 app.wechat-rendering-cache）：
        /// 只清 Profile 下的固定缓存叶子与小程序的编译缓存，聊天数据库、账号、接收文件一律不碰。
        /// </summary>
        private static void ScanWeChatCaches(CleanCategory cat, Settings s)
        {
            string ra = RoamingAppData;
            string[] roots = new string[] { @"Tencent\xwechat\radium\web\profiles", @"Tencent\WeChat\radium\web\profiles" };
            string[] leaves = new string[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
                                             "GrShaderCache", "GraphiteDawnCache", @"Shared Dictionary\cache" };
            for (int r = 0; r < roots.Length; r++)
            {
                string root = Path.Combine(ra, roots[r]);
                string brand = r == 0 ? Lang.T("微信 Weixin") : Lang.T("微信 WeChat");
                string[] profs;
                try { profs = Directory.GetDirectories(root); }
                catch (Exception) { profs = new string[0]; }
                for (int i = 0; i < profs.Length && cat.Items.Count < 1200; i++)
                {
                    SliceTick(Lang.T("检查微信缓存 · ") + Path.GetFileName(profs[i]));
                    for (int j = 0; j < leaves.Length; j++)
                    {
                        AddDirItem(cat, brand + " · " + Path.GetFileName(profs[i]) + "\\" + leaves[j],
                                   Path.Combine(profs[i], leaves[j]), CleanKind.DirContents, 0, s.UseRecycleBin);
                    }
                }
                // 小程序编译缓存：users\<账号>\applet\codecache（数据库与包在同级目录，不会误伤）
                string users = Path.Combine(ra, r == 0 ? @"Tencent\xwechat\radium\users" : @"Tencent\WeChat\radium\users");
                string[] us;
                try { us = Directory.GetDirectories(users); }
                catch (Exception) { us = new string[0]; }
                for (int i = 0; i < us.Length && cat.Items.Count < 1200; i++)
                {
                    AddDirItem(cat, brand + Lang.T(" · 小程序编译缓存"), Path.Combine(us[i], @"applet\codecache"),
                               CleanKind.DirContents, 0, s.UseRecycleBin);
                }
            }
            AddDirItem(cat, Lang.T("微信 WmpfCache"), Path.Combine(ra, @"Tencent\WeChat\radium\WmpfCache"), CleanKind.DirContents, 0, s.UseRecycleBin);
        }

        /// <summary>已卸载软件的残留：空目录、空文件、空注册表键、孤儿目录、失效快捷方式，按组汇总成一行。</summary>
        private static CleanCategory ScanResidual(Action<string> report)
        {
            CleanCategory cat = NewCat("residual", Lang.T("卸载残留"), Lang.T("已卸载软件留下的空目录、空文件、空注册表键、旧目录与失效快捷方式；默认不勾选，逐组确认后再删"), "🧩", false);
            R(report, Lang.T("扫描已卸载软件的残留..."));
            List<OrphanGroup> gs = null;
            try
            {
                gs = Orphans.Scan(null,
                    delegate (int pc, string msg) { SliceSet(pc / 100.0, msg); },
                    delegate (double f, string msg) { SliceSet(f, msg); });
            }
            catch (Exception) { return cat; }
            if (gs == null) return cat;
            for (int i = 0; i < gs.Count; i++)
            {
                OrphanGroup g = gs[i];
                if (g.Items.Count == 0) continue;
                long b = 0;
                for (int j = 0; j < g.Items.Count; j++) b += g.Items[j].Bytes;
                CleanItem it = new CleanItem();
                it.CategoryId = cat.Id;
                it.Name = g.Name + "（" + g.Items.Count + Lang.T(" 项）");
                it.Path = g.Desc;
                it.Kind = CleanKind.OrphanGroup;
                it.Bytes = b > 0 ? b : 1;
                it.Data = g;
                cat.Items.Add(it);
            }
            return cat;
        }

        private static CleanCategory ScanInstallers(Action<string> report)
        {
            CleanCategory cat = NewCat("installer", Lang.T("旧安装包"), Lang.T("下载目录中 3 天前的 msi/exe 安装包"), "💿", true);
            R(report, Lang.T("扫描旧安装包..."));
            string dl = Downloads;
            if (!Directory.Exists(dl)) return cat;
            long total = 0;
            int count = 0;
            try
            {
                string[] files = Directory.GetFiles(dl);
                DateTime cutoff = DateTime.Now.AddDays(-3);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext != ".msi" && ext != ".exe" && ext != ".msix" && ext != ".appx") continue;
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        if (fi.LastWriteTime < cutoff && fi.Length > 5 * 1024 * 1024)
                        {
                            total += fi.Length;
                            count++;
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            if (count > 0)
            {
                CleanItem it = new CleanItem();
                it.CategoryId = cat.Id;
                it.Name = Lang.T("下载目录中的旧安装包");
                it.Path = dl;
                it.Kind = CleanKind.OldFilesExt;
                it.Ext = ".msi,.exe,.msix,.appx";
                it.OlderThanDays = 3;
                it.MinBytes = 5 * 1024 * 1024;
                it.Bytes = total;
                it.Files = count;
                it.Recycle = true;
                it.Exists = true;
                it.Note = Lang.T("仅处理大于 5 MB 的安装包");
                cat.Items.Add(it);
            }
            return cat;
        }

        private static readonly string[] ArtifactNames = new string[] { "node_modules", "target", "dist", "build", ".next", "venv", ".venv", "__pycache__", "obj" };

        private static CleanCategory ScanProjects(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("projects", Lang.T("项目构建产物"), Lang.T("项目目录中的 node_modules、target、dist 等可重建产物"), "🧱", false);
            R(report, Lang.T("扫描项目构建产物..."));
            for (int d = 0; d < s.ProjectDirs.Count; d++)
            {
                string root = s.ProjectDirs[d];
                if (!Directory.Exists(root)) continue;
                ScanProjectRoot(cat, root, 0, 3);
            }
            return cat;
        }

        private static void ScanProjectRoot(CleanCategory cat, string dir, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string[] entries;
            try { entries = Directory.GetDirectories(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length && cat.Items.Count < 400; i++)
            {
                string sub = entries[i];
                string name = Path.GetFileName(sub).ToLowerInvariant();
                bool isArtifact = false;
                for (int a = 0; a < ArtifactNames.Length; a++) { if (name == ArtifactNames[a]) { isArtifact = true; break; } }
                if (isArtifact)
                {
                    if (!Guard.CanDelete(sub, true)) continue;
                    long size = FileUtil.SizeOf(sub);
                    if (size < 1024 * 1024) continue;
                    CleanItem it = new CleanItem();
                    it.CategoryId = cat.Id;
                    it.Name = Path.GetFileName(Path.GetDirectoryName(sub)) + " · " + Path.GetFileName(sub);
                    it.Path = sub;
                    it.Kind = CleanKind.DirItself;
                    it.Bytes = size;
                    it.Recycle = true;
                    it.Exists = true;
                    cat.Items.Add(it);
                    continue;
                }
                if (name == ".git" || name == "bin") continue;
                ScanProjectRoot(cat, sub, depth + 1, maxDepth);
            }
        }

        private static void AddFileItem(CleanCategory cat, string name, string path, bool recycle) { AddFileItem(cat, name, path, recycle, false); }

        /// <summary>optSensitive=true 时跳过“敏感段”拦截（Cookie 这类用户显式确认才清的项）。</summary>
        private static void AddFileItem(CleanCategory cat, string name, string path, bool recycle, bool optSensitive)
        {
            if (path == null || path.Length == 0) return;
            if (!File.Exists(path)) return;
            if (!Guard.CanDelete(path, !optSensitive)) return;
            CleanItem it = new CleanItem();
            it.CategoryId = cat.Id;
            it.Name = name;
            it.Path = path;
            it.Kind = CleanKind.FileItself;
            it.Recycle = recycle;
            it.Exists = true;
            try { it.Bytes = new FileInfo(path).Length; it.Files = 1; }
            catch (Exception) { return; }
            cat.Items.Add(it);
        }

        /// <summary>扫描目录内指定扩展名的文件（可按天数过滤），体积在扫描阶段算好。</summary>
        private static void AddExtItem(CleanCategory cat, string name, string dir, string ext, int days, bool recycle)
        {
            if (dir == null || dir.Length == 0) return;
            if (!Directory.Exists(dir)) return;
            if (!Guard.CanDelete(dir, true)) return;
            string[] exts = ext.Split(',');
            DateTime cutoff = DateTime.Now.AddDays(-days);
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { return; }
            long total = 0;
            int count = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string e = Path.GetExtension(files[i]).ToLowerInvariant();
                bool match = false;
                for (int j = 0; j < exts.Length; j++) { if (exts[j] == e) { match = true; break; } }
                if (!match) continue;
                try
                {
                    FileInfo fi = new FileInfo(files[i]);
                    if (days > 0 && fi.LastWriteTime >= cutoff) continue;
                    total += fi.Length;
                    count++;
                }
                catch (Exception) { }
            }
            if (count == 0) return;
            CleanItem it = new CleanItem();
            it.CategoryId = cat.Id;
            it.Name = name;
            it.Path = dir;
            it.Kind = CleanKind.OldFilesExt;
            it.Ext = ext;
            it.OlderThanDays = days;
            it.Bytes = total;
            it.Files = count;
            it.Recycle = recycle;
            it.Exists = true;
            cat.Items.Add(it);
        }

        /// <summary>系统痕迹缓存：缩略图、图标、通知、远程桌面等，删除后系统自动重建。</summary>
        private static CleanCategory ScanTraces(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("traces", Lang.T("系统痕迹缓存"), Lang.T("缩略图与图标数据库、远程桌面缓存、主题缩略图；删除后由系统自动重建"), "", true);
            R(report, Lang.T("扫描系统痕迹缓存..."));
            string la = LocalAppData;
            string ra = RoamingAppData;
            AddExtItem(cat, Lang.T("缩略图与图标缓存"), Path.Combine(la, @"Microsoft\Windows\Explorer"), ".db", 0, false);
            AddDirItem(cat, Lang.T("资源管理器图标缓存"), Path.Combine(la, @"Microsoft\Windows\Caches"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, Lang.T("远程桌面连接缓存"), Path.Combine(la, @"Microsoft\Terminal Server Client\Cache"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("主题缩略图缓存"), Path.Combine(ra, @"Microsoft\Windows\Themes\CachedFiles"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("缩略图缓存目录(旧版)"), Path.Combine(la, @"Microsoft\Windows\Explorer\ThumbCacheToDelete"), CleanKind.DirContents, 0, false);
            return cat;
        }

        /// <summary>使用痕迹：会清掉“最近用过什么”，默认不勾选。</summary>
        private static CleanCategory ScanUsage(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("usage", Lang.T("使用痕迹（可选）"), Lang.T("最近文档、通知历史、浏览器历史与常用站点；清掉后这些列表清空"), "", true);
            R(report, Lang.T("扫描使用痕迹..."));
            string la = LocalAppData;
            string ra = RoamingAppData;
            AddDirItem(cat, Lang.T("最近使用的文档"), Path.Combine(ra, @"Microsoft\Windows\Recent"), CleanKind.DirContents, 0, true);
            AddDirItem(cat, Lang.T("通知中心历史"), Path.Combine(la, @"Microsoft\Windows\Notifications"), CleanKind.DirContents, 0, true);
            R(report, Lang.T("扫描浏览痕迹..."));
            string[] ubs = new string[] { @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data" };
            for (int b = 0; b < ubs.Length; b++)
            {
                string ud = Path.Combine(la, ubs[b]);
                string[] profs = new string[] { "Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5" };
                for (int k = 0; k < profs.Length; k++)
                {
                    string pd = Path.Combine(ud, profs[k]);
                    AddFileItem(cat, Lang.T("浏览历史") + " · " + profs[k], Path.Combine(pd, "History"), true);
                    AddFileItem(cat, Lang.T("常用站点") + " · " + profs[k], Path.Combine(pd, "Top Sites"), true);
                    AddFileItem(cat, Lang.T("访问记录") + " · " + profs[k], Path.Combine(pd, "Visited Links"), true);
                }
            }
            string ff = Path.Combine(ra, @"Mozilla\Firefox\Profiles");
            try
            {
                if (Directory.Exists(ff))
                {
                    string[] prof2 = Directory.GetDirectories(ff);
                    for (int i2 = 0; i2 < prof2.Length; i2++)
                        AddFileItem(cat, Lang.T("浏览历史") + " · Firefox", Path.Combine(prof2[i2], "places.sqlite"), true);
                }
            }
            catch (Exception) { }
            return cat;
        }

        /// <summary>系统级缓存与日志：Windows 临时、更新缓存、事件日志、字体缓存、转储、旧系统备份等。</summary>
        /// <summary>浏览器 Cookie 数据库：删除后所有网站需要重新登录，属高风险项，默认不勾选。</summary>
        private static CleanCategory ScanCookies(Action<string> report)
        {
            CleanCategory cat = NewCat("cookie", Lang.T("浏览器 Cookie"),
                Lang.T("删掉会让这些网站退出登录；具体影响见每一项的说明，默认不勾选"), "", false);
            R(report, Lang.T("扫描浏览器 Cookie..."));
            string la = LocalAppData;
            string ra = RoamingAppData;
            string[] ubs = new string[] { @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data" };
            for (int b = 0; b < ubs.Length; b++)
            {
                string ud = Path.Combine(la, ubs[b]);
                string[] profs = new string[] { "Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5" };
                for (int k = 0; k < profs.Length; k++)
                {
                    string pd = Path.Combine(ud, profs[k], "Network");
                    AddFileItem(cat, Lang.T("Cookie 数据库") + " · " + profs[k], Path.Combine(pd, "Cookies"), true, true);
                }
            }
            string ff = Path.Combine(ra, @"Mozilla\Firefox\Profiles");
            try
            {
                if (Directory.Exists(ff))
                {
                    string[] prof2 = Directory.GetDirectories(ff);
                    for (int i2 = 0; i2 < prof2.Length; i2++)
                        AddFileItem(cat, Lang.T("Cookie 数据库") + " · Firefox", Path.Combine(prof2[i2], "cookies.sqlite"), true, true);
                }
            }
            catch (Exception) { }
            // Cookie 删掉等于这些网站退出登录，一律按高风险项标红；
            // 同时给每一项挂上完整的影响说明，别只说“需要重新登录”就完事
            string impact = Lang.T("• 这些网站会退出登录，需要重新输入账号或走一次验证\n") +
                Lang.T("• 购物车、站点记住的语言/主题/显示偏好会重置\n") +
                Lang.T("• 部分网站的“记住此设备”会失效，可能要重新做短信/邮箱验证\n") +
                Lang.T("• 保存的密码不受影响：密码存在浏览器的密码数据库（Login Data）里，不在 Cookies 里\n") +
                Lang.T("• 不会删除书签、浏览历史、下载文件与聊天记录");
            for (int i3 = 0; i3 < cat.Items.Count; i3++)
            {
                cat.Items[i3].HighRisk = true;
                cat.Items[i3].Impact = impact;
            }
            return cat;
        }

        private static CleanCategory ScanSystem(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("sys", Lang.T("系统缓存与日志"), Lang.T("Windows 临时文件、更新下载缓存、事件日志、字体缓存、内存转储与旧系统备份"), "", true);
            cat.AdminOnly = true;
            if (!Native.IsAdmin()) cat.Desc = Lang.T("当前未以管理员身份运行，系统级缓存无法扫描；右键“以管理员身份运行”后可见");
            R(report, Lang.T("扫描系统级缓存..."));
            bool admin = Native.IsAdmin();

            if (admin)
            {
                AddDirItem(cat, Lang.T("Windows 临时目录"), @"C:\Windows\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("Windows 更新下载缓存"), @"C:\Windows\SoftwareDistribution\Download", CleanKind.DirContents, 0, true);
                AddDirItem(cat, Lang.T("Windows 系统日志"), @"C:\Windows\Logs", CleanKind.DirContents, 0, true);
                AddDirItem(cat, Lang.T("组件服务日志 CBS"), @"C:\Windows\Logs\CBS", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("镜像服务日志 DISM"), @"C:\Windows\Logs\DISM", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("Windows 更新日志"), @"C:\Windows\Logs\WindowsUpdate", CleanKind.DirContents, 0, false);
                AddExtItem(cat, Lang.T("Windows 事件日志"), @"C:\Windows\System32\winevt\Logs", ".evtx", 0, true);
                AddDirItem(cat, Lang.T("字体缓存（系统）"), @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache", CleanKind.DirContents, 0, false);
                AddFileItem(cat, Lang.T("完整内存转储 MEMORY.DMP"), @"C:\Windows\MEMORY.DMP", false);
                AddDirItem(cat, Lang.T("小型内存转储 Minidump"), @"C:\Windows\Minidump", CleanKind.DirContents, 0, false);
                AddExtItem(cat, Lang.T("预读取 Prefetch"), @"C:\Windows\Prefetch", ".pf", 0, false);
                AddDirItem(cat, Lang.T("系统错误报告 WER"), @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("系统错误报告队列"), @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", CleanKind.DirContents, 0, false);
                AddExtItem(cat, Lang.T("系统安装日志 Panther"), @"C:\Windows\Panther", ".log,.etl,.xml", 0, false);
                AddExtItem(cat, Lang.T("Windows 安装器日志"), @"C:\Windows\Installer", ".log", 7, true);
                AddExtItem(cat, Lang.T("驱动安装日志 setupapi"), @"C:\Windows\INF", ".log", 7, true);
                AddDirItem(cat, Lang.T("系统服务日志 LogFiles"), @"C:\Windows\System32\LogFiles", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("网络服务临时目录"), @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("NVIDIA 驱动安装包缓存"), @"C:\ProgramData\NVIDIA Corporation\Downloader", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("NVIDIA 安装器残留"), @"C:\ProgramData\NVIDIA Corporation\Installer2", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("驱动解压临时目录 C:\\Drivers"), @"C:\Drivers", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("AMD 驱动安装包"), @"C:\AMD", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("Intel 驱动安装包"), @"C:\Intel", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("LocalService 临时目录"), @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("内核报告 LiveKernelReports"), @"C:\Windows\LiveKernelReports", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("交付优化缓存"), @"C:\ProgramData\Microsoft\Network\Downloader", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("更新协调日志"), @"C:\ProgramData\USOShared\Logs", CleanKind.DirContents, 0, true);
                AddDirItem(cat, Lang.T("诊断数据缓存"), @"C:\ProgramData\Microsoft\Diagnosis", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("零售演示内容"), @"C:\ProgramData\Microsoft\Windows\RetailDemo", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("驱动仓库临时文件"), @"C:\Windows\System32\DriverStore\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("Defender 扫描历史"), @"C:\ProgramData\Microsoft\Windows Defender\Scans\History\Results", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("错误报告临时目录"), @"C:\ProgramData\Microsoft\Windows\WER\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("更新重启缓存"), @"C:\Windows\SoftwareDistribution\PostRebootEventCache", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("更新数据库日志"), @"C:\Windows\SoftwareDistribution\DataStore\Logs", CleanKind.DirContents, 0, true);
                AddDirItem(cat, Lang.T("Intel 驱动安装缓存"), @"C:\ProgramData\Intel\Package Cache", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("Windows 升级临时目录"), @"C:\Windows\SoftwareDistribution\Download\SharedFileCache", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("传递优化缓存"), @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("传递优化日志"), @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Logs", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("OneDrive 日志"), Path.Combine(LocalAppData, @"Microsoft\OneDrive\logs"), CleanKind.OldFilesInDir, 7, true);

                // 多用户临时目录（只清其他账号的 Temp，自己那份已在“临时文件”里）
                string usersRoot = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "Users");
                string[] userDirs;
                try { userDirs = Directory.GetDirectories(usersRoot); }
                catch (Exception) { userDirs = new string[0]; }
                string ownTemp = Guard.Norm(Temp);
                for (int i = 0; i < userDirs.Length; i++)
                {
                    string t2 = Path.Combine(userDirs[i], "AppData", "Local", "Temp");
                    if (Guard.Norm(t2) == ownTemp) continue;
                    if (!Directory.Exists(t2)) continue;
                    AddDirItem(cat, Lang.T("其他账号临时目录 · ") + Path.GetFileName(userDirs[i]), t2, CleanKind.DirContents, 0, false);
                }
            }

            // 不需要管理员的部分
            AddDirItem(cat, Lang.T("字体缓存（当前用户）"), Path.Combine(LocalAppData, "FontCache"), CleanKind.DirContents, 0, false);
            AddDirItem(cat, Lang.T("Windows 安装缓存 Panther(用户)"), Path.Combine(LocalAppData, "Panther"), CleanKind.DirContents, 0, false);
            return cat;
        }

        /// <summary>深度清理：体积大但影响系统功能的项目，默认不勾选，需要手动开启。</summary>
        private static CleanCategory ScanDeep(Settings s, Action<string> report)
        {
            CleanCategory cat = NewCat("deep", Lang.T("系统深度清理"), Lang.T("旧系统备份、搜索索引等大体积项，删除后相关功能会重建索引（默认不勾选）"), "", false);
            cat.AdminOnly = true;
            R(report, Lang.T("扫描深度清理项..."));
            if (Native.IsAdmin())
            {
                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive");
                if (sysDrive == null || sysDrive.Length == 0) sysDrive = "C:";
                // 删了 Windows.old 就失去系统回滚/卸载更新的退路，属于不可逆操作：不预先勾选
                AddDirItem(cat, Lang.T("Windows.old 旧系统备份"), @"C:\Windows.old", CleanKind.DirItself, 0, false, true);
                AddDirItem(cat, Lang.T("系统升级镜像 $WINDOWS.~BT"), sysDrive + @"\$WINDOWS.~BT", CleanKind.DirItself, 0, false);
                AddDirItem(cat, Lang.T("系统升级临时 $WINDOWS.~WS"), sysDrive + @"\$WINDOWS.~WS", CleanKind.DirItself, 0, false);
                AddDirItem(cat, Lang.T("系统组件缓存 WinSxS"), @"C:\Windows\WinSxS\Temp", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("搜索索引数据库"), @"C:\ProgramData\Microsoft\Search\Data", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("安装包缓存 Package Cache"), @"C:\ProgramData\Package Cache", CleanKind.DirContents, 0, false);
                AddDirItem(cat, Lang.T("MSI 补丁缓存 $PatchCache$"), @"C:\Windows\Installer\$PatchCache$", CleanKind.DirContents, 0, false);
            }
            return cat;
        }

        /// <summary>
        /// 大体积日志与转储：只列出单个 64 MB 以上、30 天未写入的诊断类文件，
        /// 明细级别由用户逐条决定，默认不勾选。
        /// </summary>
        private static CleanCategory ScanBigLogs(Action<string> report)
        {
            CleanCategory cat = NewCat("biglog", Lang.T("大体积日志与转储"), Lang.T("单文件大于 48 MB、3 天未更新的日志/转储；逐条审阅，默认不勾选"), "", false);
            R(report, Lang.T("扫描大体积日志文件..."));
            AddBigFiles(cat, @"C:\Windows\Logs", 3);
            AddBigFiles(cat, @"C:\Windows\System32\WDI", 2);
            AddBigFiles(cat, @"C:\Windows\Temp", 2);
            AddBigFiles(cat, @"C:\ProgramData", 2);
            string la = LocalAppData;
            AddBigFiles(cat, Path.Combine(la, "Temp"), 2);
            AddBigFiles(cat, Path.Combine(la, @"Microsoft\Windows\WER"), 2);
            AddBigFiles(cat, la, 4);
            AddBigFiles(cat, RoamingAppData, 4);
            AddBigFiles(cat, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 4);
            return cat;
        }

        private static readonly string[] BigLogExts = new string[] { ".log", ".etl", ".dmp", ".mdmp", ".cab", ".evtx", ".wer" };

        private static void AddBigFiles(CleanCategory cat, string dir, int depth)
        {
            if (dir == null || dir.Length == 0) return;
            if (!Directory.Exists(dir)) return;
            if (depth < 0) return;
            if (cat.Items.Count > 60) return;
            SliceTick(Lang.T("检查大文件 · ") + Path.GetFileName(dir));
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { files = new string[0]; }
            DateTime cutoff = DateTime.Now.AddDays(-3);
            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                if (!BigLogExtsContain(Path.GetExtension(f))) continue;
                try
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length < 48L * 1024 * 1024) continue;
                    if (fi.LastWriteTime >= cutoff) continue;
                    if (!Guard.CanDelete(f, true)) continue;
                    CleanItem it = new CleanItem();
                    it.CategoryId = cat.Id;
                    it.Name = Path.GetFileName(Path.GetDirectoryName(f)) + "\\" + Path.GetFileName(f);
                    it.Path = f;
                    it.Kind = CleanKind.FileItself;
                    it.Bytes = fi.Length;
                    it.Files = 1;
                    it.Recycle = false;
                    it.Exists = true;
                    it.Note = Lang.T("最后写入 ") + fi.LastWriteTime.ToString("yyyy-MM-dd") + Lang.T("，大小 ") + Byt.Format(fi.Length);
                    cat.Items.Add(it);
                    if (cat.Items.Count > 60) return;
                }
                catch (Exception) { }
            }
            if (depth == 0) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < subs.Length; i++)
            {
                AddBigFiles(cat, subs[i], depth - 1);
            }
        }

        private static bool BigLogExtsContain(string ext)
        {
            string e = ext == null ? "" : ext.ToLowerInvariant();
            for (int i = 0; i < BigLogExts.Length; i++) if (BigLogExts[i] == e) return true;
            return false;
        }

        private static CleanCategory ScanRecycleBin(Action<string> report)
        {
            // 清空回收站不可撤销，属于“必须用户自己点”的一项：扫描出来但不预先勾选
            CleanCategory cat = NewCat("recycle", Lang.T("回收站"), Lang.T("回收站中已删除的文件（勾选后彻底清空，清空不可撤销）"), "🗑", false);
            R(report, Lang.T("检查回收站..."));
            long size = 0;
            int count = 0;
            if (Shell.RecycleBinInfo(ref size, ref count) && size > 0)
            {
                CleanItem it = new CleanItem();
                it.CategoryId = cat.Id;
                it.Name = Lang.T("回收站");
                it.Path = "recycle:";
                it.Kind = CleanKind.RecycleBin;
                it.Bytes = size;
                it.Files = count;
                it.Recycle = false;
                it.OptIn = true;
                it.HighRisk = true;
                it.Note = Lang.T("不可撤销，需手动勾选");
                it.Exists = true;
                cat.Items.Add(it);
            }
            return cat;
        }

        /// <summary>执行清理，返回实际释放字节。</summary>
        public static long Execute(List<CleanItem> items, out List<string> log, out int errors)
        {
            return Execute(items, out log, out errors, null);
        }

        public static long Execute(List<CleanItem> items, out List<string> log, out int errors, StepProgress prog)
        {
            long freed = 0;
            errors = 0;
            Planned = 0;
            // 预计量按“用户在清单里勾选、且会被真正删除”的条目统计，和确认框里的数字同源；
            // 缺少权限、被占用或已不存在的条目会计入差额，完成页据此给出解释
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].Recycle && items[i].Bytes > 0) Planned += items[i].Bytes;
            }
            log = new List<string>();
            int n = Math.Max(1, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                CleanItem it = items[i];
                // 本条目在总进度里占的一段：[basePct, basePct + spanPct)，条目内部的删除动作
                // 也折算进这一段，避免一个几 GB 的目录离手时进度一下跳 20 个点
                int basePct = 1 + ((i * 92) / n);
                int spanPct = Math.Max(1, (92 / n));
                int curPct = basePct;
                string label = it.Name;
                if (label.Length > 24) label = label.Substring(0, 24) + "…";
                if (prog != null) Report(prog, basePct, Lang.T("正在清理 ") + (i + 1) + "/" + items.Count + " · " + label);

                Action<int, int> sub = delegate (int done, int total)
                {
                    if (prog == null || total <= 0) return;
                    double f = (double)done / total;
                    if (f > 0.99) f = 0.99;
                    int pct = basePct + (int)(spanPct * f);
                    if (pct <= curPct) return;
                    curPct = pct;
                    Report(prog, pct, Lang.T("正在清理 ") + (i + 1) + "/" + items.Count + " · " + label);
                };

                // 条目里统计体积（几 GB 目录要逐个文件走）时也给一句“活着”的说明
                long tickMs = 0;
                FileUtil.SizeHook = delegate (long bytes)
                {
                    long now = Environment.TickCount;
                    if (now - tickMs < 300) return;
                    tickMs = now;
                    if (prog != null) Report(prog, curPct, Lang.T("正在统计并删除 · ") + label + " · " + Byt.Format(bytes));
                };

                if (!it.Exists && it.Kind != CleanKind.OrphanGroup) continue;
                if (it.Kind == CleanKind.OrphanGroup)
                {
                    OrphanGroup g = it.Data as OrphanGroup;
                    if (g == null || g.Items.Count == 0) continue;
                    List<string> lg = null;
                    int err2 = 0;
                    long freed2 = 0;
                    try { freed2 = Orphans.Execute(g.Items, out lg, out err2); }
                    catch (Exception) { errors++; continue; }
                    freed += freed2;
                    errors += err2;
                    if (lg != null) for (int k = 0; k < lg.Count; k++) log.Add(lg[k]);
                    continue;
                }
                if (it.Kind == CleanKind.RecycleBin)
                {
                    int hr = -1;
                    bool ok = Shell.EmptyRecycleBin(out hr);
                    if (ok)
                    {
                        freed += it.Bytes;
                        log.Add(Lang.T("已清空回收站 ") + Theme_Size(it.Bytes));
                        OpLog.Write("emptied", it.Path, it.Bytes);
                    }
                    else
                    {
                        errors++;
                        string code = hr == -1 ? "" : " (HRESULT 0x" + unchecked((uint)hr).ToString("X8") + ")";
                        log.Add(Lang.T("回收站已被其他安全软件拦截，无法清空。请把本程序加入其信任区后重试。") + code);
                    }
                    continue;
                }
                if (!Guard.CanDelete(it.Path, true) && it.Kind != CleanKind.OldFilesExt)
                {
                    log.Add(Lang.T("已跳过（受保护）：") + it.Path);
                    continue;
                }
                long before = 0;
                if (it.Kind == CleanKind.DirContents) before = FileUtil.DeleteContents(it.Path, it.Recycle, ref errors, sub);
                else if (it.Kind == CleanKind.DirItself)
                {
                    long size = FileUtil.SizeOf(it.Path);
                    if (it.Recycle && Native.Recycle(new List<string>(new string[] { it.Path }))) before = size;
                    else
                    {
                        FileUtil.ClearReadOnly(it.Path);
                        if (!Directory.Exists(it.Path)) before = size;
                        else
                        {
                            long after = FileUtil.SizeOf(it.Path);
                            if (after < size) before = size - after;
                            errors++;
                        }
                    }
                }
                else if (it.Kind == CleanKind.OldFilesInDir)
                {
                    before = FileUtil.DeleteOldFiles(it.Path, DateTime.Now.AddDays(-it.OlderThanDays), it.Recycle, ref errors, sub);
                }
                else if (it.Kind == CleanKind.FileItself)
                {
                    long size = FileUtil.SizeOf(it.Path);
                    if (it.Recycle && Native.Recycle(new List<string>(new string[] { it.Path }))) before = size;
                    else
                    {
                        FileUtil.ClearReadOnly(it.Path);
                        if (!File.Exists(it.Path)) before = size; else errors++;
                    }
                }
                else if (it.Kind == CleanKind.OldFilesExt)
                {
                    before = DeleteFilesByExt(it.Path, it.OlderThanDays, it.Ext, it.MinBytes);
                }
                freed += before;
                FileUtil.SizeHook = null;
                if (before > 0)
                {
                    log.Add(it.Name + "  " + Theme_Size(before));
                    OpLog.Write("clean", it.Path, before);
                }
            }
            FileUtil.SizeHook = null;
            Report(prog, 100, Lang.T("清理完成"));
            return freed;
        }

        private static long DeleteFilesByExt(string dir, int days, string ext, long minBytes)
        {
            long freed = 0;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { return 0; }
            DateTime cutoff = DateTime.Now.AddDays(-days);
            string[] exts = ext.Split(',');
            List<string> targets = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                string e = Path.GetExtension(files[i]).ToLowerInvariant();
                bool match = false;
                for (int j = 0; j < exts.Length; j++) { if (exts[j] == e) { match = true; break; } }
                if (!match) continue;
                try
                {
                    FileInfo fi = new FileInfo(files[i]);
                    if (days > 0 && fi.LastWriteTime >= cutoff) continue;
                    if (fi.Length < minBytes) continue;
                    targets.Add(files[i]);
                    freed += fi.Length;
                }
                catch (Exception) { }
            }
            if (targets.Count > 0 && Native.Recycle(targets)) return freed;
            long realFreed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                long sz = 0;
                try { sz = new FileInfo(targets[i]).Length; } catch (Exception) { }
                FileUtil.ClearReadOnly(targets[i]);
                if (!File.Exists(targets[i])) realFreed += sz;
            }
            return realFreed;
        }

        public static string Theme_Size(long b)
        {
            return Byt.Format(b);
        }
    }

    public static class Byt
    {
        public static string Format(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double b = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024.0; i++; }
            if (i == 0) return ((long)b).ToString() + " B";
            if (b >= 100) return b.ToString("0") + " " + u[i];
            if (b >= 10) return b.ToString("0.0") + " " + u[i];
            return b.ToString("0.00") + " " + u[i];
        }
    }
}

