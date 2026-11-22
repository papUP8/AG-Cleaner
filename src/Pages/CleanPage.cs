using System;
using System.Collections.Generic;
using System.Drawing;
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
        private List<Chip> _chips = new List<Chip>();
        private List<CleanCategory> _cats = new List<CleanCategory>();
        private string _filter = "all";
        private string _status = Lang.T("已就绪，点「开始扫描」检查可清理项");
        private bool _busy;
        private Spinner _spinner;
        private ProgressBar2 _prog;
        private long _lastCleanEnd;
        private long _lastFreed = 0;
        private Panel _cta;
        private Btn _ctaBtn;
        private Label _ctaHint;
        private static bool _everScanned = false;
        private Panel _done;
        private Label _doneTitle;
        private Label _doneSub;
        private bool _doneClean;

        public CleanPage()
        {
            _list = new VList();
            _list.ShowCheck = true;
            _list.RightHeader = Lang.T("体积");
            _list.EmptyText = Lang.T("还没有扫描结果");
            _list.EmptyHint = Lang.T("点上方「开始扫描」，检查完再逐项决定删除");
            _list.CheckChanged += delegate { UpdateSummary(); };
            _list.RowActivated += delegate { OpenDetail(); };
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

            _cleanBtn = new Btn();
            _cleanBtn.Kind = ButtonKind.Accent;
            _cleanBtn.Text = Lang.T("清理所选");
            _cleanBtn.Enabled = false;
            _cleanBtn.Size = new Size(128, 38);
            _cleanBtn.Click += delegate { ExecuteClean(); };
            _bar.Controls.Add(_cleanBtn);

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
            bool needLayout = !_prog.Visible;
            _prog.Visible = true;
            _prog.SetProgress(percent, caption);
            _prog.BackColor = Theme.Bg;
            if (needLayout) LayoutChildren();
            _prog.BringToFront();
        }

        private void HideProgress()
        {
            if (_prog == null || !_prog.Visible) return;
            _prog.Visible = false;
            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _scanBtn.SetBounds(0, y, 96, 32);
            _allBtn.SetBounds(104, y + 1, 96, 30);
            _invBtn.SetBounds(206, y + 1, 64, 30);
            int cx = 216;
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
                // 进度条放在列表上方，扫一眼就能看到，不躲在底部操作栏后
                _prog.SetBounds(0, listTop, w, 44);
                listTop += 52;
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
            _spinner.Spin(true);
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _prog.Parent = this;
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
            LayoutChildren();
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
                    r.Sub = it.Note.Length > 0 ? it.Path + "  ·  " + it.Note : it.Path;
                    r.Right = Byt.Format(it.Bytes);
                    r.Tag = c.Name;
                    r.TagColor = TagColor(c.Id);
                    r.Checked = c.DefaultOn;
                    r.Checkable = true;
                    r.Data = it;
                    r.Data2 = c;
                    rows.Add(r);
                }
            }
            _list.SetRows(rows);
            _list.Invalidate();
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
            if (_list != null) _list.Visible = !idle && !doneView;
            if (_doneTitle != null && doneView)
                _doneTitle.Text = Lang.T("清理完成 · 释放 ") + Byt.Format(_lastFreed);
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
            _cleanBtn.SetBounds(_bar.Width - 148, 10, 128, 38);
        }

        /// <summary>清理结束后回到干净状态：清空清单与分类，再返回概览页。</summary>
        private void ResetAfterClean(long freed)
        {
            _cats = new List<CleanCategory>();
            for (int i = 0; i < _chips.Count; i++) Controls.Remove(_chips[i]);
            _chips.Clear();
            _filter = "all";
            _lastFreed = freed;
            _doneClean = true;
            _list.Clear();
            _list.EmptyText = Lang.T("本次清理已完成");
            _list.EmptyHint = Lang.T("如需再次查看可清理项，请点击上方“重新扫描”；本次运行期间不会自动重扫");
            _status = Lang.T("清理完成，本次释放 ") + Byt.Format(freed) + Lang.T("；已回到概览页");
            _bar.Invalidate();
            UpdateSummary();
            MainForm mf = FindForm() as MainForm;
            if (mf != null) mf.SelectPage(0);
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

            long bytes = 0;
            bool recycle = false;
            for (int i = 0; i < selected.Count; i++)
            {
                bytes += selected[i].Bytes;
                if (selected[i].Recycle) recycle = true;
            }
            bool hasCookie = false;
            for (int ci = 0; ci < selected.Count; ci++) if (selected[ci].CategoryId == "cookie") hasCookie = true;
            string mode = recycle ? Lang.T("所选项目将移入回收站（可恢复，但不释放磁盘空间）。") : Lang.T("所选项目将被永久删除。");
            if (hasCookie) mode += Lang.T("\n注意：清单里有 Cookie 数据库，删除后这些网站的登录状态全部失效，需要重新登录。");
            bool ok = Dlg.Confirm(FindForm(), Lang.T("确认清理 ") + selected.Count + Lang.T(" 项？"),
                Lang.T("预计释放 ") + Byt.Format(bytes) + "。\n" + mode + Lang.T("\n受保护目录、白名单路径与敏感目录已自动排除，不会出现在清单里。"),
                Lang.T("开始清理"), false);
            if (!ok) return;

            _busy = true;
            _everScanned = true;
            UpdateIdleState();
            _spinner.Spin(true);
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _status = Lang.T("正在清理...");
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
                        _cleanBtn.Enabled = true;
                        UpdateIdleState();
                        _status = Lang.T("已释放 ") + Byt.Format(f) + (err > 0 ? "，" + err + Lang.T(" 项被占用或跳过") : "") + Lang.T("；点“重新扫描”查看最新清单");
                        _bar.Invalidate();

                        List<string> lines = new List<string>();
                        if (lg != null)
                        {
                            for (int i = 0; i < lg.Count; i++)
                            {
                                lines.Add(lg[i]);
                            }
                        }
                        string summary = Lang.T("释放空间 ") + Byt.Format(f) + Lang.T(" · 处理 ") + sel + Lang.T(" 项 · 明细 ") + lines.Count + Lang.T(" 条");
                        string hint = err > 0
                            ? Lang.T("有 ") + err + Lang.T(" 项被系统占用或无权删除，已记入日志，可选择重启后再清理")
                            : Lang.T("全部处理完成；来源路径与体积已写入操作日志，可在历史记录页导出 CSV");
                        Dlg.Report(FindForm(), Lang.T("清理完成"), summary, lines, hint);
                        ResetAfterClean(f);
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


