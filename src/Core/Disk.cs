using System;
using System.Collections.Generic;
using System.IO;

namespace CCDesk.Core
{
    public class DiskEntry
    {
        public string Name = "";
        public string Path = "";
        public long Bytes = 0;
        public int Files = 0;
        public bool IsDir = false;
    }

    public class DiskScanResult
    {
        public string Path = "";
        public long Total = 0;
        public int Files = 0;
        public List<DiskEntry> Entries = new List<DiskEntry>();
        public List<DiskEntry> TopFiles = new List<DiskEntry>();
    }

    /// <summary>磁盘占用分析：逐层下钻 + 大文件排行。</summary>
    public static class Disk
    {
        public static List<string> Drives()
        {
            List<string> list = new List<string>();
            try
            {
                DriveInfo[] di = DriveInfo.GetDrives();
                for (int i = 0; i < di.Length; i++)
                {
                    try
                    {
                        if (di[i].DriveType != DriveType.Fixed) continue;
                        if (!di[i].IsReady) continue;
                        list.Add(di[i].RootDirectory.FullName);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            if (list.Count == 0) list.Add("C:\\");
            return list;
        }

        public static DiskScanResult Scan(string dir, Action<string> report, bool withTopFiles)
        {
            return Scan(dir, report, withTopFiles, null, null);
        }

        public static DiskScanResult Scan(string dir, Action<string> report, bool withTopFiles,
            Action<DiskEntry> onItem, Action<int, int> onProgress)
        {
            DiskScanResult r = new DiskScanResult();
            r.Path = dir;
            if (!Directory.Exists(dir)) return r;

            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return r; }

            List<DiskEntry> list = new List<DiskEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    bool isDir = Directory.Exists(entries[i]);
                    bool link = false;
                    if (isDir)
                    {
                        FileAttributes attr = File.GetAttributes(entries[i]);
                        link = (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                    }
                    DiskEntry e = new DiskEntry();
                    e.Name = Path.GetFileName(entries[i]);
                    e.Path = entries[i];
                    e.IsDir = isDir;
                    if (link)
                    {
                        e.Files = 0;
                        r.Entries.Add(e);
                        continue;
                    }
                    list.Add(e);
                }
                catch (Exception) { }
            }

            if (report != null) report(dir);
            int done = 0;
            int count = list.Count;
            System.Threading.Tasks.Parallel.For(0, count, delegate (int i)
            {
                DiskEntry e = list[i];
                long size = 0;
                int fc = 0;
                try
                {
                    if (e.IsDir) Disk.SizeOf(e.Path, ref size, ref fc, 0, null);
                    else { size = new FileInfo(e.Path).Length; fc = 1; }
                }
                catch (Exception) { }
                e.Bytes = size;
                e.Files = fc;
                if (onItem != null)
                {
                    try { onItem(e); }
                    catch (Exception) { }
                }
                int n = System.Threading.Interlocked.Increment(ref done);
                if (onProgress != null)
                {
                    try { onProgress(n, count); }
                    catch (Exception) { }
                }
            });

            long total = 0;
            int files = 0;
            for (int i = 0; i < list.Count; i++)
            {
                total += list[i].Bytes;
                files += list[i].Files;
                r.Entries.Add(list[i]);
            }
            r.Total = total;
            r.Files = files;
            r.Entries.Sort(delegate (DiskEntry a, DiskEntry b) { return b.Bytes.CompareTo(a.Bytes); });

            if (withTopFiles)
            {
                List<DiskEntry> big = new List<DiskEntry>();
                CollectTopFiles(dir, big, 0, 40000);
                big.Sort(delegate (DiskEntry a, DiskEntry b) { return b.Bytes.CompareTo(a.Bytes); });
                if (big.Count > 40) big.RemoveRange(40, big.Count - 40);
                r.TopFiles = big;
            }
            return r;
        }

        public static void SizeOf(string path, ref long total, ref int files, int depth, Action<string> report)
        {
            if (depth > 32) return;
            string[] entries;
            try
            {
                if (!Directory.Exists(path))
                {
                    try { total += new FileInfo(path).Length; files++; } catch (Exception) { }
                    return;
                }
                entries = Directory.GetFileSystemEntries(path);
            }
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
                        SizeOf(e, ref total, ref files, depth + 1, null);
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

        private static void CollectTopFiles(string dir, List<DiskEntry> list, int depth, int budget)
        {
            if (depth > 24 || list.Count > budget) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return; }
            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    FileAttributes attr = File.GetAttributes(entries[i]);
                    if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        CollectTopFiles(entries[i], list, depth + 1, budget);
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(entries[i]);
                        if (fi.Length < 20L * 1024 * 1024) continue;
                        DiskEntry e = new DiskEntry();
                        e.Name = fi.Name;
                        e.Path = fi.FullName;
                        e.Bytes = fi.Length;
                        list.Add(e);
                        if (list.Count > budget + 400) return;
                    }
                }
                catch (Exception) { }
            }
        }

        public static double UsedRatio(string root)
        {
            try
            {
                DriveInfo di = new DriveInfo(root);
                double total = di.TotalSize;
                if (total <= 0) return 0;
                return (total - di.TotalFreeSpace) / total;
            }
            catch (Exception) { return 0; }
        }

        public static long FreeSpace(string root)
        {
            try { return new DriveInfo(root).AvailableFreeSpace; }
            catch (Exception) { return 0; }
        }

        public static long TotalSpace(string root)
        {
            try { return new DriveInfo(root).TotalSize; }
            catch (Exception) { return 0; }
        }
    }
}
