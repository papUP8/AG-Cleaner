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
        public bool AdminNeeded = false;
        public string Note = "";
        public long MinBytes = 0;
        public bool Exists = false;
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
            if (recycle && Native.Recycle(safe))
            {
                for (int i = 0; i < safe.Count; i++) freed += SizeOf(safe[i]);
                return freed;
            }
            for (int i = 0; i < safe.Count; i++)
            {
                long before = SizeOf(safe[i]);
                ClearReadOnly(safe[i]);
                if (!File.Exists(safe[i]) && !Directory.Exists(safe[i])) freed += before;
                else if (Native.DelayDelete(safe[i])) freed += before;
                else errors++;
            }
            return freed;
        }

        /// <summary>删除若干“早于 cutoff”的文件。</summary>
        public static long DeleteOldFiles(string dir, DateTime cutoff, bool recycle, ref int errors)
        {
            List<string> targets = new List<string>();
            long total = 0;
            CollectOldFiles(dir, cutoff, targets, ref total, 0);
            if (targets.Count == 0) return 0;
            if (recycle)
            {
                if (Native.Recycle(targets)) return total;
            }
            long freed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                long before = 0;
                try { before = new FileInfo(targets[i]).Length; } catch (Exception) { }
                ClearReadOnly(targets[i]);
                if (!File.Exists(targets[i])) freed += before;
                else if (Native.DelayDelete(targets[i])) freed += before;
                else errors++;
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

        /// <summary>扫描阶段的分段进度：前 30% 用于列举候选项，后 70% 用于统计体积。</summary>
        private class ScanCtx
        {
            public StepProgress P;
            public int Step;
            public int Total = 13;
            public void Begin(string text) { Report(P, 4 + Step * 32 / Total, text); }
            public Action<string> Msg(string text) { return delegate (string m) { Report(P, 4 + Step * 32 / Total, text); }; }
            public void Next() { Step++; Report(P, 4 + Step * 32 / Total, Lang.T("已完成分类列举 ") + Step + "/" + Total); }
        }

        public static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string RoamingAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string Temp { get { return Environment.GetEnvironmentVariable("TEMP"); } }
        public static string Downloads { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); } }

        /// <summary>各类扫描耗时（诊断用，--scan 时一并写出）。</summary>
        public static List<string> Timings = new List<string>();

        private static CleanCategory Timed(string name, Func<CleanCategory> f)
        {
            DateTime t0 = DateTime.Now;
            CleanCategory c = f();
            Timings.Add(name + "\t" + ((int)(DateTime.Now - t0).TotalMilliseconds) + " ms\t" + c.Items.Count);
            return c;
        }

        public static List<CleanCategory> Scan(Settings s, Action<string> report)
        {
            return Scan(s, delegate (int pc, string msg) { if (report != null) report(msg); });
        }

        public static List<CleanCategory> Scan(Settings s, StepProgress prog)
        {
            List<CleanCategory> cats = new List<CleanCategory>();
            Timings.Clear();
            ScanCtx c = new ScanCtx();
            c.P = prog;
            Report(prog, 3, Lang.T("正在列举可清理目录..."));

            cats.Add(Timed("临时文件", delegate { return ScanUserTemp(s, c.Msg(Lang.T("扫描临时文件与临时目录..."))); })); c.Next();
            cats.Add(Timed("浏览器缓存", delegate { return ScanBrowser(s, c.Msg(Lang.T("扫描浏览器缓存..."))); })); c.Next();
            cats.Add(Timed("应用缓存", delegate { return ScanApps(s, c.Msg(Lang.T("扫描应用缓存与 UWP 缓存..."))); })); c.Next();
            cats.Add(Timed("系统痕迹", delegate { return ScanTraces(s, c.Msg(Lang.T("扫描缩略图与图标数据库..."))); })); c.Next();
            cats.Add(Timed("系统缓存与日志", delegate { return ScanSystem(s, c.Msg(Lang.T("扫描系统缓存与日志..."))); })); c.Next();
            cats.Add(Timed("深度清理", delegate { return ScanDeep(s, c.Msg(Lang.T("扫描旧系统备份与组件缓存..."))); })); c.Next();
            if (s.CleanGpuShaders) cats.Add(Timed("着色器", delegate { return ScanGpu(c.Msg(Lang.T("扫描显卡着色器缓存..."))); }));
            cats.Add(Timed("日志与错误报告", delegate { return ScanLogsReports(c.Msg(Lang.T("扫描崩溃转储与错误报告..."))); })); c.Next();
            cats.Add(Timed("使用痕迹", delegate { return ScanUsage(s, c.Msg(Lang.T("扫描使用痕迹..."))); })); c.Next();
            cats.Add(Timed("浏览器 Cookie", delegate { return ScanCookies(c.Msg(Lang.T("扫描浏览器 Cookie..."))); })); c.Next();
            if (s.DeleteOldInstallers) cats.Add(Timed("旧安装包", delegate { return ScanInstallers(c.Msg(Lang.T("扫描下载目录的旧安装包..."))); }));
            cats.Add(Timed("大体积日志", delegate { return ScanBigLogs(c.Msg(Lang.T("扫描大体积日志与转储文件..."))); })); c.Next();
            cats.Add(Timed("回收站", delegate { return ScanRecycleBin(c.Msg(Lang.T("检查回收站..."))); })); c.Next();
            // 残留清理并进深度清理，用户只需一个清单、一次点击
            cats.Add(Timed("卸载残留", delegate { return ScanResidual(c.Msg(Lang.T("扫描已卸载软件的残留..."))); })); c.Next();

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
                Report(prog, 36, Lang.T("正在统计 ") + all.Count + Lang.T(" 个项目的体积..."));
                int done = 0;
                int lastPct = 36;
                System.Threading.Tasks.Parallel.For(0, all.Count, delegate (int idx)
                {
                    CleanItem it = all[idx];
                    try { SizeItem(it); }
                    catch (Exception) { }
                    int d = System.Threading.Interlocked.Increment(ref done);
                    int pct = 36 + d * 60 / all.Count;
                    if (pct > lastPct)
                    {
                        System.Threading.Interlocked.Exchange(ref lastPct, pct);
                        string label = it.Name;
                        if (label.Length > 26) label = label.Substring(0, 26) + "…";
                        Report(prog, pct, Lang.T("统计体积 ") + d + "/" + all.Count + " · " + label);
                    }
                });
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
            Report(prog, 99, Lang.T("整理清单..."));
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
            return new string[] { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache", @"Service Worker\ScriptCache" };
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

            string[] rootLeaves = new string[] { "ShaderCache", "GrShaderCache", "Crashpad", "BrowserMetrics" };
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

        /// <summary>已卸载软件的残留：空目录、空文件、空注册表键、孤儿目录、失效快捷方式，按组汇总成一行。</summary>
        private static CleanCategory ScanResidual(Action<string> report)
        {
            CleanCategory cat = NewCat("residual", Lang.T("卸载残留"), Lang.T("已卸载软件留下的空目录、空文件、空注册表键、旧目录与失效快捷方式；默认不勾选，逐组确认后再删"), "🧩", false);
            R(report, Lang.T("扫描已卸载软件的残留..."));
            List<OrphanGroup> gs = null;
            try { gs = Orphans.Scan(null, null); }
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
            CleanCategory cat = NewCat("cookie", Lang.T("浏览器 Cookie（高风险，可选）"), Lang.T("删除后所有网站都要重新登录；默认不勾选，确认后再删"), "", false);
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
                AddDirItem(cat, Lang.T("Windows.old 旧系统备份"), @"C:\Windows.old", CleanKind.DirItself, 0, false);
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
            CleanCategory cat = NewCat("recycle", Lang.T("回收站"), Lang.T("回收站中已删除的文件（勾选后彻底清空，清空不可撤销）"), "🗑", true);
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
            log = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                CleanItem it = items[i];
                if (prog != null)
                {
                    string label = it.Name;
                    if (label.Length > 24) label = label.Substring(0, 24) + "…";
                    Report(prog, 1 + ((i * 96) / Math.Max(1, items.Count)), Lang.T("正在清理 ") + (i + 1) + "/" + items.Count + " · " + label);
                }
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
                    if (Shell.EmptyRecycleBin())
                    {
                        freed += it.Bytes;
                        log.Add(Lang.T("已清空回收站 ") + Theme_Size(it.Bytes));
                        OpLog.Write("emptied", it.Path, it.Bytes);
                    }
                    else
                    {
                        errors++;
                        log.Add(Lang.T("回收站已被其他安全软件拦截，无法清空。请把本程序加入其信任区后重试。"));
                    }
                    continue;
                }
                if (!Guard.CanDelete(it.Path, true) && it.Kind != CleanKind.OldFilesExt)
                {
                    log.Add(Lang.T("已跳过（受保护）：") + it.Path);
                    continue;
                }
                long before = 0;
                if (it.Kind == CleanKind.DirContents) before = FileUtil.DeleteContents(it.Path, it.Recycle, ref errors);
                else if (it.Kind == CleanKind.DirItself)
                {
                    long size = FileUtil.SizeOf(it.Path);
                    if (it.Recycle && Native.Recycle(new List<string>(new string[] { it.Path }))) before = size;
                    else
                    {
                        FileUtil.ClearReadOnly(it.Path);
                        if (!Directory.Exists(it.Path)) before = size; else errors++;
                    }
                }
                else if (it.Kind == CleanKind.OldFilesInDir)
                {
                    before = FileUtil.DeleteOldFiles(it.Path, DateTime.Now.AddDays(-it.OlderThanDays), it.Recycle, ref errors);
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
                if (before > 0)
                {
                    log.Add(it.Name + "  " + Theme_Size(before));
                    OpLog.Write("clean", it.Path, before);
                }
            }
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
                else if (Native.DelayDelete(targets[i])) realFreed += sz;
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

