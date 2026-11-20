using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    public class AnalyzePage : PageBase
    {
        public override string PageTitle { get { return "磁盘分析"; } }
        public override string PageSubtitle { get { return "逐层下钻查看目录占用，支持大文件排行与回收站删除"; } }

        private VList _list;
        private ActionBar _bar;
        private Btn _drive;
        private Btn _up;
        private Btn _reveal;
        private Btn _modeBtn;
        private Spinner _spinner;

        private List<string> _drives = new List<string>();
        private int _driveIdx = 0;
        private string _path = "";
        private DiskScanResult _result;
        private bool _busy;
        private readonly List<DiskEntry> _live = new List<DiskEntry>();
        private readonly object _liveLock = new object();
        private int _liveDone;
        private int _liveTotal;
        private int _lastRefresh;
        private bool _bigMode = false;
        private string _status = "选择一个磁盘开始扫描";
        private long _totalBytes = 0;
        private int _totalFiles = 0;

        public AnalyzePage()
        {
            _drives = Disk.Drives();
            _path = _drives.Count > 0 ? _drives[0] : "C:\\";

            _drive = new Btn();
            _drive.Kind = ButtonKind.Subtle;
            _drive.Text = _path;
            _drive.Size = new Size(90, 32);
            _drive.Click += delegate
            {
                if (_drives.Count == 0) return;
                _driveIdx = (_driveIdx + 1) % _drives.Count;
                _path = _drives[_driveIdx];
                _drive.Text = _path;
                Navigate(_path);
            };
            Controls.Add(_drive);

            _up = new Btn();
            _up.Kind = ButtonKind.Ghost;
            _up.Text = "上一级";
            _up.Size = new Size(78, 32);
            _up.Click += delegate
            {
                string parent = Path.GetDirectoryName(_path.TrimEnd('\\'));
                if (parent == null || parent.Length < 2) return;
                Navigate(parent);
            };
            Controls.Add(_up);

            _reveal = new Btn();
            _reveal.Kind = ButtonKind.Link;
            _reveal.Text = "打开位置";
            _reveal.Size = new Size(80, 30);
            _reveal.Click += delegate { Native.RevealInExplorer(_path); };
            Controls.Add(_reveal);

            _modeBtn = new Btn();
            _modeBtn.Kind = ButtonKind.Ghost;
            _modeBtn.Text = "大文件排行";
            _modeBtn.Size = new Size(110, 32);
            _modeBtn.Click += delegate { _bigMode = !_bigMode; ShowResult(); };
            Controls.Add(_modeBtn);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = "占用";
            _list.RowHeight = 56;
            _list.EmptyText = "尚未扫描";
            _list.EmptyHint = "点击左侧磁盘按钮选择驱动器";
            _list.RowActivated += delegate { OpenSelected(); };
            _list.ActionClicked += delegate { DeleteSelected(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _drive.SetBounds(0, y, 90, 32);
            _up.SetBounds(98, y, 78, 32);
            _reveal.SetBounds(184, y + 1, 80, 30);
            _modeBtn.SetBounds(w - 110, y, 110, 32);
            _bar.SetBounds(0, Height - 58, w, 58);
            _list.SetBounds(0, y + 44, w, Height - (y + 44) - 92);
            _spinner.SetBounds(w - 160, y - 6, 44, 44);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            if (_result == null) Navigate(_path);
        }

        public void Navigate(string path)
        {
            if (_busy) return;
            if (!Directory.Exists(path)) return;
            _busy = true;
            _bigMode = false;
            _spinner.Spin(true);
            lock (_liveLock) { _live.Clear(); }
            _liveDone = 0;
            _liveTotal = 0;
            _lastRefresh = 0;
            _status = "正在扫描 " + path + " ...";
            _list.Clear();
            _bar.Invalidate();
            string target = path;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DiskScanResult res = null;
                try
                {
                    res = Disk.Scan(target, delegate (string msg)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { _status = msg; _bar.Invalidate(); }); }
                        catch (Exception) { }
                    }, true, delegate (DiskEntry e)
                    {
                        lock (_liveLock) { _live.Add(e); }
                        try { BeginInvoke((MethodInvoker)delegate { OnLiveItem(); }); }
                        catch (Exception) { }
                    }, delegate (int n, int total)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { _liveDone = n; _liveTotal = total; _bar.Invalidate(); }); }
                        catch (Exception) { }
                    });
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _path = target;
                        _result = res;
                        if (res != null)
                        {
                            _totalBytes = res.Total;
                            _totalFiles = res.Files;
                            _status = "共 " + Byt.Format(res.Total) + " · " + res.Files + " 个文件";
                        }
                        ShowResult();
                    });
                }
                catch (Exception) { }
            });
        }

        private void ShowResult()
        {
            if (_result == null) return;
            List<VRow> rows = new List<VRow>();
            _modeBtn.Text = _bigMode ? "返回目录" : "大文件排行";
            _modeBtn.Invalidate();
            _modeBarInvalidate();

            if (_bigMode)
            {
                for (int i = 0; i < _result.TopFiles.Count; i++)
                {
                    DiskEntry e = _result.TopFiles[i];
                    VRow r = new VRow();
                    r.Title = e.Name;
                    r.Sub = Theme.ShortPath(e.Path, 96);
                    r.Right = Byt.Format(e.Bytes);
                    r.Tag = "大文件";
                    r.TagColor = Theme.Warn;
                    r.Fraction = _totalBytes > 0 ? (double)e.Bytes / _totalBytes : 0;
                    r.BarColor = Theme.Warn;
                    r.Action = "删除";
                    r.ActionColor = Theme.Danger;
                    r.Data = e;
                    rows.Add(r);
                }
                _list.EmptyText = "没有发现 20 MB 以上的大文件";
                _list.EmptyHint = "";
            }
            else
            {
                _list.SetRows(BuildDirRows(_result.Entries));
                _list.EmptyText = "空目录";
                _list.EmptyHint = "";
            }
            _list.SetRows(rows);
            _bar.Invalidate();
        }

        // 扫描过程中的增量刷新：一有条目统计完就先显示，避免整盘扫描时长时间空白
        private void OnLiveItem()
        {
            int now = Environment.TickCount;
            if (_liveDone < _liveTotal && now - _lastRefresh < 400) return;
            _lastRefresh = now;
            List<DiskEntry> snap = new List<DiskEntry>();
            lock (_liveLock) { snap.AddRange(_live); }
            long sum = 0;
            for (int i = 0; i < snap.Count; i++) sum += snap[i].Bytes;
            _status = "已统计 " + _liveDone + "/" + _liveTotal + " 项 · 累计 " + Byt.Format(sum);
            _list.SetRows(BuildDirRows(snap));
            _bar.Invalidate();
        }

        private List<VRow> BuildDirRows(List<DiskEntry> entries)
        {
            List<VRow> rows = new List<VRow>();
            List<DiskEntry> sorted = new List<DiskEntry>(entries);
            sorted.Sort(delegate (DiskEntry a, DiskEntry b) { return b.Bytes.CompareTo(a.Bytes); });
            long max = 0;
            for (int i = 0; i < sorted.Count; i++) if (sorted[i].Bytes > max) max = sorted[i].Bytes;
            for (int i = 0; i < sorted.Count; i++)
            {
                DiskEntry e = sorted[i];
                VRow r = new VRow();
                r.Title = e.Name + (e.IsDir ? "\\" : "");
                r.Sub = e.IsDir ? (e.Files + " 个文件") : "文件";
                r.Right = Byt.Format(e.Bytes);
                r.Tag = e.IsDir ? "文件夹" : "文件";
                r.TagColor = e.IsDir ? Theme.Blue : Theme.Muted;
                r.Fraction = max > 0 ? (double)e.Bytes / max : 0;
                r.BarColor = e.Bytes > 1024L * 1024 * 1024 ? Theme.Danger : Theme.Blue;
                r.Action = "删除";
                r.ActionColor = Theme.Danger;
                r.Data = e;
                rows.Add(r);
            }
            return rows;
        }

        private void _modeBarInvalidate()
        {
            _bar.Invalidate();
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Theme.Txt(g, Theme.ShortPath(_path, 90), Theme.F(9.5f, FontStyle.Bold), Theme.Text, 4, 10);
            Theme.Txt(g, _status, Theme.F(8f), Theme.Muted, 4, 33);
            using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
            {
                Theme.Txt(g, "双击文件夹下钻 · 删除默认进回收站", Theme.F(8f), Theme.Hint, new Rectangle(0, 32, _bar.Width - 8, 20), sf);
            }
        }

        private void OpenSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            DiskEntry e = r.Data as DiskEntry;
            if (e == null) return;
            if (e.IsDir && !_bigMode) Navigate(e.Path);
            else Native.RevealInExplorer(e.Path);
        }

        private void DeleteSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            DiskEntry e = r.Data as DiskEntry;
            if (e == null) return;
            if (!Guard.CanDelete(e.Path, false))
            {
                Dlg.Info(FindForm(), "受保护路径", "该路径属于系统或受保护位置，CCDesk 不会删除。");
                return;
            }
            bool ok = Dlg.Confirm(FindForm(), "删除该项？",
                e.Path + "\n\n体积：" + Byt.Format(e.Bytes) + "\n项目将移入回收站，可从回收站还原。",
                "移入回收站", true);
            if (!ok) return;
            long size = e.Bytes;
            int errors = 0;
            long freed = 0;
            if (Native.Recycle(new List<string>(new string[] { e.Path }))) freed = size;
            else
            {
                FileUtil.ClearReadOnly(e.Path);
                if (!File.Exists(e.Path) && !Directory.Exists(e.Path)) freed = size;
                else errors++;
            }
            OpLog.Write("delete", e.Path, freed);
            Dlg.Info(FindForm(), "已删除", errors > 0 ? "部分文件被占用，未能删除" : ("已释放 " + Byt.Format(freed)));
            Navigate(_path);
        }
    }
}


