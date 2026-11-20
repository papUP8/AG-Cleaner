using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
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
        public override string PageTitle { get { return "深度清理"; } }
        public override string PageSubtitle { get { return "按安全目录清单扫描，逐项审阅后删除；受保护路径自动跳过"; } }

        private VList _list;
        private ActionBar _bar;
        private Btn _scanBtn;
        private Btn _cleanBtn;
        private Btn _allBtn;
        private Btn _invBtn;
        private List<Chip> _chips = new List<Chip>();
        private List<CleanCategory> _cats = new List<CleanCategory>();
        private string _filter = "all";
        private string _status = "正在统计可清理项目，稍候即出清单";
        private bool _busy;
        private Spinner _spinner;
        private long _lastFreed = 0;

        public CleanPage()
        {
            _list = new VList();
            _list.ShowCheck = true;
            _list.RightHeader = "体积";
            _list.EmptyText = "还没有扫描结果";
            _list.EmptyHint = "扫描会自动开始，看到清单后再逐项决定删除";
            _list.CheckChanged += delegate { UpdateSummary(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _scanBtn = new Btn();
            _scanBtn.Kind = ButtonKind.Subtle;
            _scanBtn.Text = "重新扫描";
            _scanBtn.Size = new Size(96, 32);
            _scanBtn.Click += delegate { StartScan(); };
            Controls.Add(_scanBtn);

            _allBtn = new Btn();
            _allBtn.Kind = ButtonKind.Link;
            _allBtn.Text = "全选";
            _allBtn.Size = new Size(52, 30);
            _allBtn.Click += delegate { _list.CheckAll(true); UpdateSummary(); };
            Controls.Add(_allBtn);

            _invBtn = new Btn();
            _invBtn.Kind = ButtonKind.Link;
            _invBtn.Text = "反选";
            _invBtn.Size = new Size(52, 30);
            _invBtn.Click += delegate { _list.InvertChecks(); UpdateSummary(); };
            Controls.Add(_invBtn);

            _cleanBtn = new Btn();
            _cleanBtn.Kind = ButtonKind.Accent;
            _cleanBtn.Text = "清理所选";
            _cleanBtn.Size = new Size(128, 38);
            _cleanBtn.Click += delegate { ExecuteClean(); };
            _bar.Controls.Add(_cleanBtn);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);
            _spinner.BringToFront();

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _scanBtn.SetBounds(0, y, 96, 32);
            _allBtn.SetBounds(104, y + 1, 52, 30);
            _invBtn.SetBounds(156, y + 1, 52, 30);
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
            _list.SetBounds(0, listTop, w, Height - listTop - 92);
            _spinner.SetBounds(w - 46, y + 4, 24, 24);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            if (_cats.Count == 0) StartScan();
        }

        public void StartScan()
        {
            if (_busy) return;
            _busy = true;
            _status = "正在扫描...";
            _list.Clear();
            _spinner.Spin(true);
            _scanBtn.Enabled = false;
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CleanCategory> result = null;
                string error = null;
                try
                {
                    Settings st = Settings.Load();
                    result = CleanEngine.Scan(st, delegate (string msg)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { _status = msg; _bar.Invalidate(); }); }
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
                        _scanBtn.Enabled = true;
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
                            _status = "扫描完成：共 " + count + " 项，可释放 " + Byt.Format(total);
                        }
                        else
                        {
                            _status = "扫描失败：" + error;
                        }
                        _bar.Invalidate();
                        LayoutChildren();
                        UpdateSummary();
                    });
                }
                catch (Exception) { }
            });
        }

        private void BuildChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                Controls.Remove(_chips[i]);
            }
            _chips.Clear();
            _filter = "all";

            Chip all = MakeChip("全部", -1, "all");
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
            Theme.Txt(g, "已选 " + count + " 项 · 预计释放 " + Byt.Format(bytes), Theme.F(9.5f, FontStyle.Bold), Theme.Text, 4, 12);
            Theme.Txt(g, _status, Theme.F(8f), Theme.Muted, 4, 34);
            _cleanBtn.SetBounds(_bar.Width - 148, 10, 128, 38);
        }

        private void ExecuteClean()
        {
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
                Dlg.Info(FindForm(), "没有选中的项目", "请先勾选需要清理的项目，或点击“全选”。");
                return;
            }

            long bytes = 0;
            bool recycle = false;
            for (int i = 0; i < selected.Count; i++)
            {
                bytes += selected[i].Bytes;
                if (selected[i].Recycle) recycle = true;
            }
            string mode = recycle ? "所选项目将移入回收站（可恢复）。" : "所选项目将被永久删除。";
            bool ok = Dlg.Confirm(FindForm(), "确认清理 " + selected.Count + " 项？",
                "预计释放 " + Byt.Format(bytes) + "。\n" + mode + "\n受保护目录、白名单路径与敏感目录已自动排除，不会出现在清单里。",
                "开始清理", false);
            if (!ok) return;

            _busy = true;
            _spinner.Spin(true);
            _status = "正在清理...";
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0;
                int errors = 0;
                List<string> log = null;
                try
                {
                    freed = CleanEngine.Execute(selected, out log, out errors);
                }
                catch (Exception ex)
                {
                    log = new List<string>(new string[] { "执行失败：" + ex.Message });
                }
                long f = freed;
                List<string> lg = log;
                int err = errors;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _status = "已释放 " + Byt.Format(f) + (err > 0 ? "，" + err + " 项被占用跳过" : "");
                        _bar.Invalidate();
                        StartScan();
                        string detail = "释放空间：" + Byt.Format(f);
                        if (lg != null && lg.Count > 0) detail += "\n\n明细（前 8 条）：\n" + Join(log, 8);
                        Dlg.Info(FindForm(), "清理完成", detail);
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
            if (list.Count > max) s += "· 其余 " + (list.Count - max) + " 条已写入操作日志";
            return s;
        }
    }
}

