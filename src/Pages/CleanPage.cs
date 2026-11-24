using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    /// <summary>筛选胶囊。</summary>
    public class Chip : Control
    {
        public bool Active = false;
        public int Count = -1;
        private bool _hover;

        public Chip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            Height = 28;
            Cursor = Cursors.Hand;
            Font = Theme.F(8.3f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public int FitWidth(Graphics g)
        {
            string s = Text + (Count >= 0 ? "  " + Count : "");
            int w = (int)g.MeasureString(s, Font).Width + 22;
            Width = w;
            return w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = Active ? Theme.Alpha(Theme.Accent, 30) : (_hover ? Theme.CardHover : Theme.CardSoft);
            Theme.FillRound(e.Graphics, r, Height / 2, bg);
            if (Active) Theme.StrokeRound(e.Graphics, r, Height / 2, Theme.Alpha(Theme.Accent, 90), 1);
            Color fg = Active ? Theme.Accent : Theme.TextDim;
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                string s = Text;
                if (Count >= 0) s = Text + "  " + Count;
                Theme.Txt(e.Graphics, s, Font, fg, r, sf);
            }
        }
    }

    public class CleanPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("深度清理"); } }
        public override string PageSubtitle { get { return Lang.T("按安全目录清单扫描，逐项审阅后删除；受保护路径自动跳过"); } }

        private VList _list;
        private ActionBar _bar;
        private Btn _scanBtn;
        private Btn _cleanBtn;
        private Btn _allBtn;
        private Btn _invBtn;
        private Btn _sortBtn;
        /// <summary>按分类过滤时，底部中央的“返回全部”按钮。</summary>
        private Btn _backBtn;
        /// <summary>0=扫描原始顺序 1=体积从大到小 2=名称 3=路径。</summary>
        private int _sortMode;
        private List<Chip> _chips = new List<Chip>();
        private List<CleanCategory> _cats = new List<CleanCategory>();
        private string _filter = "all";
        private string _status = Lang.T("已就绪，点「开始扫描」检查可清理项");
        private bool _busy;
        private Spinner _spinner;
        private ProgressBar2 _prog;
        private System.Windows.Forms.Timer _ease;  // 进度缓动定时器
        private double _shownPct = 0;        // 界面当前显示的百分比
        private int _targetPct = 0;          // 引擎上报的真实百分比
        private string _progCaption = "";
        private long _progSetMs = 0;
        private long _lastCleanEnd;
        private long _lastFreed = 0;
        /// <summary>本次清理实际释放的体积；单独存一份，避免清单清空后被重算成 0。</summary>
        private long _doneFreed = 0;
        private long _donePlan = 0;    // 确认框里给用户看的预计释放量
        private long _doneActual = 0;  // 真正删掉、磁盘上实际减少的量
        private Panel _cta;
        private Btn _ctaBtn;
        private Label _ctaHint;
        private static bool _everScanned = false;
        private Panel _done;
        private Label _doneTitle;
        private Label _doneSub;
        private bool _doneClean;

        /// <summary>扫描是否已结束并拿到清单（供离屏截图脚本等待，不影响界面逻辑）。</summary>
        public bool ScanDone { get { return !_busy && _cats.Count > 0; } }

        /// <summary>诊断用：把清单滚到指定行，便于离屏截图核对高风险项的配色。</summary>
        public void ScrollListTo(int rowIndex)
        {
            _list.SetScroll(rowIndex * _list.RowHeight);
            _list.Invalidate();
        }

        /// <summary>诊断用：暴露清单分类，便于命令行核对默认勾选规则。</summary>
        public List<CleanCategory> Categories { get { return _cats; } }

        /// <summary>诊断用：取当前清单可见行对应的条目，用于离屏渲染详情弹窗。</summary>
        public CleanItem RowItem(int index)
        {
            VRow r = _list.RowAt(index);
            return r == null ? null : r.Data as CleanItem;
        }

        /// <summary>诊断用：命令行直接指定排序维度，验证排序后的顺序。</summary>
        public void SetSort(int mode)
        {
            _sortMode = ((mode % 4) + 4) % 4;
            if (_sortBtn != null) _sortBtn.Text = SortLabel();
            FillList();
            UpdateSummary();
            _bar.Invalidate();
        }

        public CleanPage()
        {
            _list = new VList();
            _list.ShowCheck = true;
            _list.RightHeader = Lang.T("体积");
            _list.EmptyText = Lang.T("还没有扫描结果");
            _list.EmptyHint = Lang.T("点上方「开始扫描」，检查完再逐项决定删除");
            _list.CheckChanged += delegate { UpdateSummary(); };
            _list.RowActivated += delegate { OpenDetail(); };
            _list.ActionClicked += delegate { OpenSelectedPath(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _scanBtn = new Btn();
            _scanBtn.Kind = ButtonKind.Subtle;
            _scanBtn.Text = Lang.T("开始扫描");
            _scanBtn.Size = new Size(96, 32);
            _scanBtn.Click += delegate { StartScan(); };
            Controls.Add(_scanBtn);

            _allBtn = new Btn();
            _allBtn.Kind = ButtonKind.Link;
            _allBtn.Text = Lang.T("全选");
            _allBtn.Size = new Size(52, 30);
            _allBtn.Click += delegate { _list.CheckAll(true); UpdateSummary(); };
            Controls.Add(_allBtn);

            _invBtn = new Btn();
            _invBtn.Kind = ButtonKind.Link;
            _invBtn.Text = Lang.T("反选");
            _invBtn.Size = new Size(52, 30);
            _invBtn.Click += delegate { _list.InvertChecks(); UpdateSummary(); };
            Controls.Add(_invBtn);

            _sortBtn = new Btn();
            _sortBtn.Kind = ButtonKind.Link;
            _sortBtn.Text = Lang.T("排序：默认");
            _sortBtn.Size = new Size(96, 30);
            _sortBtn.Click += delegate { CycleSort(); };
            _bar.Controls.Add(_sortBtn);

            _cleanBtn = new Btn();
            _cleanBtn.Kind = ButtonKind.Accent;
            _cleanBtn.Text = Lang.T("清理所选");
            _cleanBtn.Enabled = false;
            _cleanBtn.Size = new Size(128, 38);
            _cleanBtn.Click += delegate { ExecuteClean(); };
            _bar.Controls.Add(_cleanBtn);

            // 按分类看清单时，底部中央给一个明显的返回入口，不用再去顶上的胶囊里找“全部”
            _backBtn = new Btn();
            _backBtn.Kind = ButtonKind.Subtle;
            _backBtn.Text = "← " + Lang.T("返回全部");
            _backBtn.Visible = false;
            _backBtn.Click += delegate { FilterTo("all"); };
            _bar.Controls.Add(_backBtn);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);
            _spinner.BringToFront();

            _prog = new ProgressBar2();
            _prog.BackColor = Theme.Bg;
            _prog.Visible = false;
            Controls.Add(_prog);
            _prog.BringToFront();

            _done = new Panel();
            _done.BackColor = Theme.Bg;
            _done.Visible = false;
            Controls.Add(_done);

            _doneTitle = new Label();
            _doneTitle.BackColor = Theme.Bg;
            _doneTitle.ForeColor = Theme.Text;
            _doneTitle.Font = Theme.F(15f, FontStyle.Bold);
            _doneTitle.TextAlign = ContentAlignment.MiddleCenter;
            _doneTitle.Text = Lang.T("清理完成");
            _done.Controls.Add(_doneTitle);

            _doneSub = new Label();
            _doneSub.BackColor = Theme.Bg;
            _doneSub.ForeColor = Theme.Muted;
            _doneSub.Font = Theme.F(9.5f);
            _doneSub.TextAlign = ContentAlignment.MiddleCenter;
            _doneSub.Text = Lang.T("垃圾已清完，接着处理这些");
            _done.Controls.Add(_doneSub);

            string[] nexts = new string[] { Lang.T("启动项优化"), Lang.T("卸载应用"), Lang.T("磁盘分析"), Lang.T("系统优化") };
            int[] targets = new int[] { 2, 1, 3, 4 };
            for (int ni = 0; ni < nexts.Length; ni++)
            {
                Btn b = new Btn();
                b.Kind = ni == 0 ? ButtonKind.Accent : ButtonKind.Subtle;
                b.Text = nexts[ni];
                b.Size = new Size(132, 42);
                int tgt = targets[ni];
                b.Click += delegate { if (MainForm.Instance != null) MainForm.Instance.SelectPage(tgt); };
                _done.Controls.Add(b);
            }
            _cta = new Panel();
            _cta.BackColor = Theme.Bg;
            Controls.Add(_cta);

            _ctaBtn = new Btn();
            _ctaBtn.Kind = ButtonKind.Accent;
            _ctaBtn.Text = Lang.T("开始扫描");
            _ctaBtn.Font = Theme.F(13f, FontStyle.Bold);
            _ctaBtn.Size = new Size(240, 56);
            _ctaBtn.Click += delegate { StartScan(); };
            _cta.Controls.Add(_ctaBtn);

            _ctaHint = new Label();
            _ctaHint.BackColor = Theme.Bg;
            _ctaHint.ForeColor = Theme.Muted;
            _ctaHint.Font = Theme.F(9.5f);
            _ctaHint.TextAlign = ContentAlignment.MiddleCenter;
            _ctaHint.Text = Lang.T("扫描不会自动开始，点按钮后再检查磁盘，两分钟左右");
            _cta.Controls.Add(_ctaHint);
            UpdateIdleState();

            LayoutChildren();
        }

        private void ShowProgress(int percent, string caption)
        {
            if (_prog == null) return;
            if (caption != null && caption.Length > 0) _progCaption = caption;
            int t = percent;
            if (t < 0) t = 0;
            if (t > 100) t = 100;
            if (t > _targetPct) _targetPct = t;
            _progSetMs = Environment.TickCount;
            bool needLayout = !_prog.Visible;
            _prog.Visible = true;
            _prog.BackColor = Theme.Bg;
            if (needLayout) LayoutChildren();
            _prog.CreateControl(); // 之前一直 Visible=false，句柄没建过，先建句柄再画
            // 本工程里子控件的绘制顺序是“索引大的画在后”，所以放到最后一个索引才是真正压在最上层
            Controls.SetChildIndex(_prog, Controls.Count - 1);
            if (_ease == null)
            {
                _ease = new System.Windows.Forms.Timer();
                _ease.Interval = 120;
                _ease.Tick += delegate { EaseTick(); };
            }
            if (!_ease.Enabled) _ease.Start();
            RenderProgress();
        }

        /// <summary>
        /// 进度缓动：引擎上报的是真实完成度，界面按 120ms 一小步往目标值走。
        /// 目的是“匀速观察”——真实值一次跳 50 个点时，环和条也会在几秒内连续走完，
        /// 而不是一帧之内从 33% 蹦到 87%。上报停了的时候最多再缓 2 个点，不谎报太多。
        /// </summary>
        private void EaseTick()
        {
            if (_prog == null || !_prog.Visible) return;
            double want = _targetPct;
            if (Environment.TickCount - _progSetMs > 900 && _targetPct < 96) want = _targetPct + 2.0;
            if (_shownPct < want)
            {
                double gap = want - _shownPct;
                double step = gap / 8.0;      // 越接近目标越慢
                if (step < 0.7) step = 0.7;
                if (step > 5.0) step = 5.0;
                _shownPct += step;
                if (_shownPct > want) _shownPct = want;
                if (_shownPct > 99.4 && _targetPct < 100) _shownPct = 99.4; // 没真正结束前不显示 100%
                RenderProgress();
            }
        }

        private void RenderProgress()
        {
            if (_prog == null || !_prog.Visible) return;
            int shown = (int)Math.Round(_shownPct);
            if (shown < 0) shown = 0;
            if (shown > 100) shown = 100;
            _prog.SetProgress(shown, _progCaption);
        }

        private void HideProgress()
        {
            if (_prog == null || !_prog.Visible) return;
            _prog.Visible = false;
            if (_ease != null) _ease.Stop();
            LayoutChildren();
        }

        /// <summary>新一轮开始前把缓动状态清零。</summary>
        private void ResetProgress()
        {
            _shownPct = 0;
            _targetPct = 0;
            _progCaption = "";
            _progSetMs = Environment.TickCount;
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _scanBtn.SetBounds(0, y, 96, 32);
            _allBtn.SetBounds(104, y + 1, 96, 30);
            _invBtn.SetBounds(206, y + 1, 64, 30);
            int cx = 278; // 从「反选」按钮右侧起排，否则第一个胶囊会被按钮压住，只剩一条边
            int cy = y + 2;
            int rows = 1;
            for (int i = 0; i < _chips.Count; i++)
            {
                Graphics g = CreateGraphics();
                int cw = _chips[i].FitWidth(g);
                g.Dispose();
                if (cx + cw > w - 8 && cx > 0)
                {
                    cx = 0;
                    cy += 34;
                    rows++;
                }
                _chips[i].SetBounds(cx, cy, cw, 28);
                cx += cw + 8;
            }
            int listTop = y + 46 + (rows - 1) * 34;
            _bar.SetBounds(0, Height - 58, w, 58);
            if (_prog != null && _prog.Visible)
            {
                // 进度改成整块居中：圆环 + 百分比落在内容区正中，进度条在其下方，不再挤在列表顶上一条
                int freeH = Height - listTop - 72;
                if (freeH < 170) freeH = 170;
                _prog.SetBounds(0, listTop, w, freeH);
            }
            _list.SetBounds(0, listTop, w, Height - listTop - 72);
            _spinner.SetBounds(w - 56, y, 44, 44);
            if (_cta != null)
            {
                int freeH = Height - listTop - 72;
                if (freeH < 120) freeH = 120;
                _cta.SetBounds(0, listTop, w, freeH);
                _ctaBtn.SetBounds((w - 240) / 2, (freeH - 56) / 2 - 16, 240, 56);
                _ctaHint.SetBounds(0, (freeH - 56) / 2 + 50, w, 22);
            }
            if (_done != null)
            {
                int freeH = Height - listTop - 72;
                if (freeH < 150) freeH = 150;
                _done.SetBounds(0, listTop, w, freeH);
                _doneTitle.SetBounds(0, freeH / 2 - 92, w, 32);
                _doneSub.SetBounds(0, freeH / 2 - 54, w, 22);
                int bw = 132, gap = 12, total = bw * 4 + gap * 3;
                int bx = (w - total) / 2;
                int by = freeH / 2 - 6;
                int nth = 0;
                for (int ci = 0; ci < _done.Controls.Count; ci++)
                {
                    Btn b = _done.Controls[ci] as Btn;
                    if (b == null) continue;
                    b.SetBounds(bx + nth * (bw + gap), by, bw, 42);
                    nth++;
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            // 不自动扫描：必须由用户点「开始扫描」，避免用户还没看到界面就开始跑磁盘
        }

        public void StartScan()
        {
            if (_busy) return;
            _doneClean = false;
            _modeReset();
            _busy = true;
            _everScanned = true;
            UpdateIdleState();
            _status = Lang.T("正在扫描...");
            _list.Clear();
            _list.SetLoading(true, Lang.T("正在扫描磁盘..."));
            _spinner.Spin(false); // 进度已改成内容区正中的大圆环，右上角的小转圈不再重复
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _prog.Parent = this;
            ResetProgress();
            ShowProgress(0, Lang.T("准备扫描..."));
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CleanCategory> result = null;
                string error = null;
                try
                {
                    Settings st = Settings.Load();
                    result = CleanEngine.Scan(st, delegate (int pc, string msg)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _status = msg;
                                ShowProgress(pc, msg);
                                _bar.Invalidate();
                            });
                        }
                        catch (Exception) { }
                    });
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        HideProgress();
                        _list.SetLoading(false, null);
                        _scanBtn.Enabled = true;
                        _scanBtn.Text = Lang.T("重新扫描");
                        _cleanBtn.Enabled = true;
                        UpdateIdleState();
                        if (result != null)
                        {
                            _cats = result;
                            BuildChips();
                            FillList();
                            long total = 0;
                            int count = 0;
                            for (int i = 0; i < _cats.Count; i++)
                            {
                                total += _cats[i].TotalBytes();
                                count += _cats[i].Items.Count;
                            }
                            _status = Lang.T("扫描完成：共 ") + count + Lang.T(" 项，可释放 ") + Byt.Format(total);
                        }
                        else
                        {
                            _status = Lang.T("扫描失败：") + error;
                        }
                        _bar.Invalidate();
                        LayoutChildren();
                        UpdateSummary();
                    });
                }
                catch (Exception) { }
            });
        }

        /// <summary>把列表空状态恢复成“等待扫描”的默认文案。</summary>
        private void _modeReset()
        {
            _list.EmptyText = Lang.T("还没有扫描结果");
            _list.EmptyHint = Lang.T("点上方「开始扫描」，检查完再逐项决定删除");
        }

        private void BuildChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                Controls.Remove(_chips[i]);
            }
            _chips.Clear();
            _filter = "all";

            Chip all = MakeChip(Lang.T("全部"), -1, "all");
            int total = 0;
            for (int i = 0; i < _cats.Count; i++) total += _cats[i].Items.Count;
            all.Count = total;
            all.Active = true;
            _chips.Add(all);

            for (int i = 0; i < _cats.Count; i++)
            {
                CleanCategory c = _cats[i];
                if (c.Items.Count == 0) continue;
                Chip chip = MakeChip(c.Name, c.Items.Count, c.Id);
                _chips.Add(chip);
            }
            UpdateBackButton();
            LayoutChildren();
        }

        /// <summary>诊断用：按分类过滤清单，离屏截图时核对某一类（如 Cookie、回收站）的排版与配色。</summary>
        public void FilterTo(string id)
        {
            _filter = id == null || id.Length == 0 ? "all" : id;
            string label = _filter == "all" ? Lang.T("全部") : CatLabel(_filter);
            for (int i = 0; i < _chips.Count; i++)
            {
                _chips[i].Active = _chips[i].Text == label;
                _chips[i].Invalidate();
            }
            FillList();
            UpdateSummary();
            UpdateBackButton();
        }

        /// <summary>只有按分类过滤时才显示底部中央的返回入口，回到“全部”后自动收起。</summary>
        private void UpdateBackButton()
        {
            if (_backBtn == null) return;
            bool show = _filter != "all" && _cats != null && _cats.Count > 0;
            _backBtn.Visible = show;
            if (show)
            {
                int total = 0;
                for (int i = 0; i < _cats.Count; i++) total += _cats[i].Items.Count;
                _backBtn.Text = "← " + Lang.T("返回全部") + " (" + total + ")";
            }
            _backBtn.Invalidate();
            if (_bar != null) _bar.Invalidate();
        }

        private Chip MakeChip(string name, int count, string id)
        {
            Chip chip = new Chip();
            chip.Text = name;
            chip.Count = count >= 0 ? count : -1;
            chip.Click += delegate
            {
                _filter = id;
                for (int i = 0; i < _chips.Count; i++)
                {
                    _chips[i].Active = _chips[i] == chip;
                    _chips[i].Invalidate();
                }
                FillList();
                UpdateSummary();
                UpdateBackButton();
            };
            Controls.Add(chip);
            chip.BringToFront();
            return chip;
        }

        private string CatLabel(string id)
        {
            for (int i = 0; i < _cats.Count; i++) if (_cats[i].Id == id) return _cats[i].Name;
            return "";
        }

        private void FillList()
        {
            List<VRow> rows = new List<VRow>();
            for (int i = 0; i < _cats.Count; i++)
            {
                CleanCategory c = _cats[i];
                if (_filter != "all" && c.Id != _filter) continue;
                for (int j = 0; j < c.Items.Count; j++)
                {
                    CleanItem it = c.Items[j];
                    VRow r = new VRow();
                    r.Title = it.Name;
                    // 有影响说明的项（Cookie 这类）把说明放在标题后面，路径留给详情页看
                    if (it.Impact.Length > 0)
                    {
                        string first = it.Impact.Split('\n')[0].Trim();
                        if (first.StartsWith("•")) first = first.Substring(1).Trim();
                        r.Sub = first;
                    }
                    else r.Sub = it.Note.Length > 0 ? it.Path + "  ·  " + it.Note : it.Path;
                    r.Right = Byt.Format(it.Bytes);
                    r.Tag = c.Name;
                    r.TagColor = TagColor(c.Id);
                    r.Risk = it.HighRisk;
                    // 勾选状态记在清单项上：切筛选、切排序都不会把用户的勾选重置掉
                    if (!it.CheckInit)
                    {
                        it.Checked = c.DefaultOn && !it.OptIn && !it.DefaultOff;
                        it.CheckInit = true;
                    }
                    r.Checked = it.Checked;
                    r.Checkable = true;
                    if (it.Kind != CleanKind.RecycleBin && it.Kind != CleanKind.OrphanGroup &&
                        (Directory.Exists(it.Path) || File.Exists(it.Path)))
                    {
                        r.Action = Lang.T("打开");
                        r.ActionColor = Theme.Blue;
                    }
                    r.Data = it;
                    r.Data2 = c;
                    rows.Add(r);
                }
            }
            if (_sortMode == 1)
            {
                rows.Sort(delegate (VRow a, VRow b)
                {
                    CleanItem x = (CleanItem)a.Data, y = (CleanItem)b.Data;
                    return y.Bytes.CompareTo(x.Bytes);
                });
            }
            else if (_sortMode == 2)
            {
                rows.Sort(delegate (VRow a, VRow b)
                {
                    CleanItem x = (CleanItem)a.Data, y = (CleanItem)b.Data;
                    return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
                });
            }
            else if (_sortMode == 3)
            {
                rows.Sort(delegate (VRow a, VRow b)
                {
                    CleanItem x = (CleanItem)a.Data, y = (CleanItem)b.Data;
                    return string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);
                });
            }
            _list.SetRows(rows);
            _list.Invalidate();
        }

        /// <summary>排序按钮在三个维度间轮换，点一次换一种，文案直接说明当前维度。</summary>
        private void CycleSort()
        {
            _sortMode = (_sortMode + 1) % 4;
            _sortBtn.Text = SortLabel();
            FillList();
            UpdateSummary();
            _bar.Invalidate();
        }

        private string SortLabel()
        {
            if (_sortMode == 1) return Lang.T("排序：体积");
            if (_sortMode == 2) return Lang.T("排序：名称");
            if (_sortMode == 3) return Lang.T("排序：路径");
            return Lang.T("排序：默认");
        }

        /// <summary>行的勾选回写到清单项，保证切筛选后不丢选择。</summary>
        private void SyncChecks()
        {
            for (int i = 0; i < _list.VisibleCount; i++)
            {
                VRow r = _list.RowAt(i);
                CleanItem it = r.Data as CleanItem;
                if (it != null) it.Checked = r.Checked;
            }
        }

        /// <summary>行内「打开」：在资源管理器里定位这一项的路径，只定位不删除。</summary>
        private void OpenSelectedPath()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            CleanItem it = r.Data as CleanItem;
            if (it == null) return;
            try
            {
                if (it.Kind == CleanKind.RecycleBin)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
                    return;
                }
                if (File.Exists(it.Path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + it.Path + "\"") { UseShellExecute = true });
                else if (Directory.Exists(it.Path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + it.Path + "\"") { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        private Color TagColor(string id)
        {
            if (id == "temp") return Theme.Warn;
            if (id == "browser") return Theme.Blue;
            if (id == "apps") return Theme.Accent;
            if (id == "traces") return Theme.Blue;
            if (id == "usage") return Theme.Muted;
            if (id == "sys") return Theme.Danger;
            if (id == "deep") return Theme.Warn;
            if (id == "gpu") return Theme.C("#8B7CF6");
            if (id == "logs") return Theme.C("#F08A4B");
            if (id == "installer") return Theme.Warn;
            if (id == "projects") return Theme.C("#4FB3D9");
            if (id == "recycle") return Theme.TextDim;
            return Theme.Muted;
        }

        private void UpdateIdleState()
        {
            bool idle = !_everScanned && _cats.Count == 0 && !_busy;
            bool doneView = _doneClean && !_busy;
            if (_done != null)
            {
                _done.Visible = doneView;
                if (doneView) _done.BringToFront();
            }
            if (_cta != null)
            {
                _cta.Visible = idle;
                if (idle) _cta.BringToFront();
            }
            if (_list != null) _list.Visible = !idle && !doneView && !_busy; // 扫描/清理期间把列表让出来，居中进度才看得见
            if (_doneTitle != null && doneView)
                _doneTitle.Text = Lang.T("清理完成 · 实际释放 ") + Byt.Format(_doneActual > 0 ? _doneActual : _donePlan);
            if (_doneSub != null && doneView)
            {
                // 主数字必须是磁盘上真实减少的量；确认时的预计值放在下一行，差额说明是被占用还是跳过
                if (_donePlan - _doneActual > 65536)
                    _doneSub.Text = Lang.T("确认时预计 ") + Byt.Format(_donePlan) + Lang.T("，其中 ") + Byt.Format(_donePlan - _doneActual) + Lang.T(" 被程序占用或跳过");
                else
                    _doneSub.Text = Lang.T("垃圾已清完，接着处理这些");
            }
            if (_scanBtn != null) _scanBtn.Visible = !idle && !doneView;
            if (_allBtn != null) _allBtn.Visible = !idle && !doneView;
            if (_invBtn != null) _invBtn.Visible = !idle && !doneView;
            if (_bar != null) _bar.Visible = !idle && !doneView;
        }

        private void OpenDetail()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            CleanItem it = r.Data as CleanItem;
            if (it == null) return;
            using (DetailForm f = new DetailForm(it))
            {
                Form host = FindForm();
                if (host != null) f.ShowDialog(host); else f.Show();
            }
        }

        private void UpdateSummary()
        {
            if (_doneClean) return; // 完成后清单已清空，这里重算只会把“释放体积”抹成 0
            SyncChecks();
            long bytes = 0;
            int count = 0;
            for (int i = 0; i < _list.VisibleCount; i++)
            {
                VRow r = _list.RowAt(i);
                if (r.Checked)
                {
                    count++;
                    CleanItem it = r.Data as CleanItem;
                    if (it != null) bytes += it.Bytes;
                }
            }
            _lastFreed = bytes;
            _bar.Invalidate();
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            // 扫描/清理进行中，底栏左侧不显示“已选 0 项 · 预计释放 0 B”这类还没意义的数字，
            // 免得用户以为选了东西或者以为估算出错；扫描完再按真实勾选显示
            if (!_busy)
            {
                long bytes = 0;
                int count = 0;
                for (int i = 0; i < _list.VisibleCount; i++)
                {
                    VRow r = _list.RowAt(i);
                    if (r.Checked)
                    {
                        count++;
                        CleanItem it = r.Data as CleanItem;
                        if (it != null) bytes += it.Bytes;
                    }
                }
                Theme.Txt(g, Lang.T("已选 ") + count + Lang.T(" 项 · 预计释放 ") + Byt.Format(bytes), Theme.F(9.5f, FontStyle.Bold), Theme.Text, 4, 12);
                Theme.Txt(g, _status, Theme.F(8f), Theme.Muted, 4, 34);
            }
            _cleanBtn.SetBounds(_bar.Width - 148, 10, 128, 38);
            if (_sortBtn != null) _sortBtn.SetBounds(_bar.Width - 148 - 102, 14, 96, 30);
            // 扫描/清理期间底栏只留状态，按钮和返回入口都收起来，避免点得动不该点的东西
            if (_cleanBtn != null) _cleanBtn.Visible = !_busy;
            if (_sortBtn != null) _sortBtn.Visible = !_busy;
            if (_backBtn != null) _backBtn.Visible = !_busy && _filter != "all" && _cats != null && _cats.Count > 0;
            if (_backBtn != null)
            {
                Graphics g2 = _bar.CreateGraphics();
                int bw2 = (int)g2.MeasureString(_backBtn.Text, _backBtn.Font).Width + 34;
                g2.Dispose();
                int bx2 = (_bar.Width - bw2) / 2;
                // 底部中央的返回按钮不能和右侧的排序/清理按钮挤在一起
                if (bx2 + bw2 > _bar.Width - 160) bx2 = _bar.Width - 160 - bw2;
                if (bx2 < 120) bx2 = 120;
                _backBtn.SetBounds(bx2, 12, bw2, 34);
            }
        }

        /// <summary>诊断用：把“清理完成”页摆到界面上（不做任何真实删除），用于离屏核对释放体积的显示。</summary>
        public void SimulateDoneForShot(long planned, long actual)
        {
            ResetAfterClean(actual, true, planned);
            UpdateIdleState();
            LayoutChildren();
        }

        public void SimulateDoneForShot(long freed)
        {
            SimulateDoneForShot(freed, freed);
        }

        /// <summary>清理结束后回到干净状态：清空清单与分类，再返回概览页。</summary>
        private void ResetAfterClean(long freed)
        {
            ResetAfterClean(freed, false);
        }

        private void ResetAfterClean(long freed, bool stayOnPage, long planned = -1)
        {
            _cats = new List<CleanCategory>();
            for (int i = 0; i < _chips.Count; i++) Controls.Remove(_chips[i]);
            _chips.Clear();
            _filter = "all";
            _doneClean = true;
            _list.Clear();
            _list.EmptyText = Lang.T("本次清理已完成");
            _list.EmptyHint = Lang.T("如需再次查看可清理项，请点击上方“重新扫描”；本次运行期间不会自动重扫");
            _status = Lang.T("清理完成，本次实际释放 ") + Byt.Format(freed) + Lang.T("；已回到概览页");
            UpdateBackButton();
            _bar.Invalidate();
            UpdateSummary();
            _doneFreed = freed; // UpdateSummary 会按空清单把 _lastFreed 归零，完成页必须用这份原始值
            _doneActual = freed;
            _donePlan = planned >= 0 ? planned : freed;
            MainForm mf = FindForm() as MainForm;
            if (mf != null && !stayOnPage) mf.SelectPage(0);
        }

        /// <summary>确认框正文：预计释放只统计会真正删除的部分，回收站与永久删除分开说清楚。</summary>
        internal static string ConfirmBody(int total, int recycleCount, long permBytes, bool hasCookie)
        {
            string mode;
            if (recycleCount <= 0) mode = Lang.T("所选项目将被永久删除。");
            else if (recycleCount >= total) mode = Lang.T("所选项目将移入回收站（可恢复）。");
            else mode = Lang.T("其中 ") + recycleCount + Lang.T(" 项移入回收站（可恢复），其余 ") + (total - recycleCount) + Lang.T(" 项永久删除。");
            if (hasCookie) mode += Lang.T("\n\n清单里有 Cookie 数据库，删除后：\n") +
                Lang.T("• 这些网站会退出登录，需要重新输入账号或走一次验证\n") +
                Lang.T("• 购物车、站点记住的语言/主题/显示偏好会重置\n") +
                Lang.T("• 部分网站的“记住此设备”会失效，可能要重新做短信/邮箱验证\n") +
                Lang.T("• 保存的密码不受影响（密码在浏览器的 Login Data 里，不在 Cookies 里）\n") +
                Lang.T("• 书签、浏览历史、下载文件与聊天记录不会被删除");
            string head;
            if (permBytes > 0) head = Lang.T("预计释放 ") + Byt.Format(permBytes) + Lang.T("（被占用的文件会自动跳过）。\n");
            else head = Lang.T("本次不会释放磁盘空间。\n");
            return head + mode + Lang.T("\n\n受保护目录、白名单路径与敏感目录已自动排除，不会出现在清单里。");
        }

        private void ExecuteClean()
        {
            // 清理刚结束时忽略连续的点击，避免“完成”按钮的残留点击再次触发清理
            if (Environment.TickCount - _lastCleanEnd < 900) return;
            List<CleanItem> selected = new List<CleanItem>();
            for (int i = 0; i < _list.VisibleCount; i++)
            {
                VRow r = _list.RowAt(i);
                if (!r.Checked) continue;
                CleanItem it = r.Data as CleanItem;
                if (it != null) selected.Add(it);
            }
            if (selected.Count == 0)
            {
                Dlg.Info(FindForm(), Lang.T("没有选中的项目"), Lang.T("请先勾选需要清理的项目，或点击“全选”。"));
                return;
            }

            long permBytes = 0;        // 其中会被真正删掉、能释放空间的体积
            int recycleCount = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].Recycle) recycleCount++;
                else permBytes += selected[i].Bytes;
            }
            bool hasCookie = false;
            for (int ci = 0; ci < selected.Count; ci++) if (selected[ci].CategoryId == "cookie") hasCookie = true;
            bool ok = Dlg.Confirm(FindForm(), Lang.T("确认清理 ") + selected.Count + Lang.T(" 项？"),
                ConfirmBody(selected.Count, recycleCount, permBytes, hasCookie),
                Lang.T("开始清理"), false);
            if (!ok) return;

            _busy = true;
            _everScanned = true;
            UpdateIdleState();
            _spinner.Spin(false); // 清理过程同样用居中的大圆环，不再额外挂小转圈
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _status = Lang.T("正在清理...");
            ResetProgress();
            ShowProgress(0, Lang.T("准备清理..."));
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0;
                int errors = 0;
                List<string> log = null;
                try
                {
                    freed = CleanEngine.Execute(selected, out log, out errors, delegate (int pc, string msg)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _status = msg;
                                ShowProgress(pc, msg);
                                _bar.Invalidate();
                            });
                        }
                        catch (Exception) { }
                    });
                }
                catch (Exception ex)
                {
                    log = new List<string>(new string[] { Lang.T("执行失败：") + ex.Message });
                }
                long f = freed;
                long plannedNow = CleanEngine.Planned;
                List<string> lg = log;
                int err = errors;
                int sel = selected.Count;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _lastCleanEnd = Environment.TickCount;
                        _spinner.Spin(false);
                        HideProgress();
                        _list.SetLoading(false, null);
                        _cleanBtn.Enabled = true;
                        UpdateIdleState();
                        _status = Lang.T("实际释放 ") + Byt.Format(f) + (err > 0 ? "，" + err + Lang.T(" 处被占用或跳过") : "") + Lang.T("；点“重新扫描”查看最新清单");
                        _bar.Invalidate();

                        List<string> lines = new List<string>();
                        if (lg != null)
                        {
                            for (int i = 0; i < lg.Count; i++)
                            {
                                lines.Add(lg[i]);
                            }
                        }
                        string summary = Lang.T("实际释放 ") + Byt.Format(f) + Lang.T(" · 处理 ") + sel + Lang.T(" 项 · 明细 ") + lines.Count + Lang.T(" 条");
                        long planned = plannedNow > 0 ? plannedNow : CleanEngine.Planned;
                        if (planned > f + 1024 * 64)
                        {
                            // 预计与实际对不上时把差额说清楚：多半是文件被程序占用、无权限或被保护规则挡住
                            summary += Lang.T("（确认时预计 ") + Byt.Format(planned) + Lang.T("，差额 ") + Byt.Format(planned - f) + Lang.T(" 被占用或跳过）");
                        }
                        string hint = err > 0
                            ? Lang.T("有 ") + err + Lang.T(" 项被系统占用或无权删除，已记入日志，可选择重启后再清理")
                            : Lang.T("全部处理完成；来源路径与体积已写入操作日志，可在历史记录页导出 CSV");
                        Dlg.Report(FindForm(), Lang.T("清理完成"), summary, lines, hint);
                        ResetAfterClean(f, false, plannedNow);
                        UpdateIdleState();
                    });
                }
                catch (Exception) { }
            });
        }

        private string Join(List<string> list, int max)
        {
            string s = "";
            for (int i = 0; i < list.Count && i < max; i++)
            {
                s += "· " + list[i] + "\n";
            }
            if (list.Count > max) s += Lang.T("· 其余 ") + (list.Count - max) + Lang.T(" 条已写入操作日志");
            return s;
        }
    }
}


