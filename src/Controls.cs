using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AGCleaner
{
    public class Panel2 : Panel
    {
        public Panel2()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>切换配色后逐级刷新自绘控件的底色，避免界面里残留另一套主题的颜色。</summary>
    public static class ThemeSync
    {
        public static void Apply(Control root)
        {
            if (root == null) return;
            Fix(root);
            for (int i = 0; i < root.Controls.Count; i++) Apply(root.Controls[i]);
        }

        /// <summary>控件所处的“表面色”：卡片内的控件用卡片色，其余跟随父容器。</summary>
        private static Color Surface(Control parent)
        {
            if (parent == null) return Theme.Bg;
            if (parent is Card) return Theme.Card;
            return parent.BackColor;
        }

        private static void Fix(Control c)
        {
            // 顶栏与侧栏由主窗体自己配色，不能按“父容器底色”覆盖
            if (c is Panel2 && c.Parent is Form) return;
            if (c is NavItem) { c.BackColor = Theme.SidebarBg; return; }
            if (c is Seg3) { c.BackColor = Theme.TitleBg; return; }
            if (c is MiniBtn) { c.BackColor = Theme.TitleBg; return; }
            if (c is ScrollView)
            {
                ScrollView sv = (ScrollView)c;
                c.BackColor = Theme.Bg;
                sv.Canvas.BackColor = Theme.Bg;
                return;
            }
            if (c is SearchBox)
            {
                c.BackColor = Surface(c.Parent);
                return;
            }
            if (c is Label) { c.BackColor = Surface(c.Parent); c.ForeColor = Theme.Muted; return; }
            if (c is TextBox)
            {
                c.BackColor = (c.Parent is SearchBox) ? Theme.Card : Theme.CardSoft;
                c.ForeColor = Theme.TextDim;
                return;
            }
            c.BackColor = Surface(c.Parent);
        }
    }

    /// <summary>圆角卡片容器，子控件通过 Padding 摆放。</summary>
    public class Card : Panel2
    {
        public int Radius = 12;
        public bool Hoverable = false;
        private bool _hover;

        public Card()
        {
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, Radius, Hoverable && _hover ? Theme.CardHover : Theme.Card);
            Theme.StrokeRound(e.Graphics, r, Radius, Theme.Border, 1);
            base.OnPaint(e);
        }
    }

    /// <summary>纯色圆角小标签。</summary>
    public class Badge : Control
    {
        public Color ForeColor2 = Theme.Muted;
        public Color BackColor2 = Theme.CardSoft;
        public string Label = "";

        public Badge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            Font = Theme.F(8f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, Height / 2, BackColor2);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, Label, Font, r, ForeColor2, flags);
        }
    }

    public enum ButtonKind { Accent, Ghost, Subtle, Danger, Link }

    /// <summary>自绘按钮，支持悬停/按下/禁用。</summary>
    public class Btn : Control
    {
        public ButtonKind Kind = ButtonKind.Ghost;
        public string Glyph = "";
        public int Radius = 8;
        public bool Compact = false;
        private bool _hover;
        private bool _down;

        public Btn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Font = Theme.F(9f);
            Height = 34;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        public void FitWidth(int extra)
        {
            Graphics g = CreateGraphics();
            int w = (int)g.MeasureString(Text, Font).Width;
            if (Glyph.Length > 0) w += 18;
            Width = w + extra;
            g.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = Theme.Card;
            Color fg = Theme.Text;
            Color border = Theme.Border;

            switch (Kind)
            {
                case ButtonKind.Accent:
                    bg = _hover ? Theme.AccentDark : Theme.Accent;
                    fg = Theme.AccentFg;
                    border = bg;
                    break;
                case ButtonKind.Ghost:
                    bg = _hover ? Theme.CardHover : Theme.Card;
                    fg = Theme.Text;
                    break;
                case ButtonKind.Subtle:
                    bg = _hover ? Theme.CardHover : Theme.CardSoft;
                    fg = Theme.TextDim;
                    border = Theme.BorderSoft;
                    break;
                case ButtonKind.Danger:
                    bg = _hover ? Theme.Danger : Theme.DangerSoft;
                    fg = Theme.Danger;
                    border = Theme.Alpha(Theme.Danger, 70);
                    break;
                case ButtonKind.Link:
                    bg = Color.Transparent;
                    fg = _hover ? Theme.Accent : Theme.TextDim;
                    border = Color.Transparent;
                    break;
            }
            if (!Enabled)
            {
                bg = Theme.Alpha(bg, 120);
                fg = Theme.Muted;
            }
            if (_down && Enabled) bg = ControlPaint.Dark(bg, 0.08f);

            if (bg != Color.Transparent)
            {
                Theme.FillRound(g, r, Radius, bg);
                if (Kind != ButtonKind.Accent && border != Color.Transparent) Theme.StrokeRound(g, r, Radius, border, 1);
            }

            string label = Text;
            int textW = (int)g.MeasureString(label, Font).Width;
            int glyphW = Glyph.Length > 0 ? 18 : 0;
            int startX = (Width - textW - glyphW) / 2;
            if (startX < 6) startX = 6;
            if (Glyph.Length > 0)
            {
                Theme.Txt(g, Glyph, Theme.F(10f), fg, startX, (Height - 16) / 2);
                startX += glyphW;
            }
            Theme.Txt(g, label, Font, fg, startX, (Height - Font.Height) / 2 - 1);
        }
    }

    /// <summary>顶栏上的胶囊小按钮（颜色跟随顶栏底色，不随内容区配色走）。</summary>
    public class TitleBtn : Control
    {
        private bool _hover;
        private bool _down;

        public TitleBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.F(9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            bool onBlue = Theme.TitleOnBlue;
            int a = _down ? 56 : (_hover ? 44 : 30);
            Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 7, onBlue ? Theme.Alpha(Color.White, a) : Theme.CardSoft);
            Theme.StrokeRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 7, onBlue ? Theme.Alpha(Color.White, 70) : Theme.BorderSoft, 1);
            Color fg = onBlue ? Theme.TitleText : Theme.TextDim;
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                Theme.Txt(g, Text, Font, fg, new Rectangle(0, 0, Width, Height), sf);
            }
        }
    }

    /// <summary>侧边栏导航项。</summary>
    public class NavItem : Control
    {
        public string Glyph = "";
        public string Sub = "";
        public bool Active = false;
        private bool _hover;

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.SidebarBg;
            Cursor = Cursors.Hand;
            Height = 52;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Rectangle r = new Rectangle(0, 2, Width, Height - 4);
            if (Active)
            {
                Theme.FillRound(g, new Rectangle(8, 2, Width - 16, Height - 4), 9, Theme.Alpha(Theme.Accent, 22));
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(b, 0, Height / 2 - 9, 3, 18);
                }
            }
            else if (_hover)
            {
                Theme.FillRound(g, new Rectangle(8, 2, Width - 16, Height - 4), 9, Theme.CardSoft);
            }
            Color fg = Active ? Theme.Text : (_hover ? Theme.TextDim : Theme.Muted);
            if (Glyph.Length > 0) Theme.Txt(g, Glyph, Theme.F(13f), Active ? Theme.Accent : fg, 20, (Height - 20) / 2);
            Theme.Txt(g, Text, Font, fg, 54, (Height - Font.Height) / 2 - 1);
        }
    }

    /// <summary>带自绘细滚动条的垂直滚动容器。</summary>
    public class ScrollView : Panel2
    {
        public Panel Canvas;
        private int _scroll;
        private int _contentHeight;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartScroll;

        public ScrollView()
        {
            BackColor = Theme.Bg;
            Canvas = new Panel();
            Canvas.BackColor = Theme.Bg;
            Canvas.SetBounds(0, 0, 100, 100);
            Controls.Add(Canvas);
        }

        public int ScrollValue { get { return _scroll; } }

        public void SetContentHeight(int h)
        {
            _contentHeight = h;
            Clamp();
            Invalidate();
        }

        private bool ScrollbarNeeded { get { return _contentHeight > ClientSize.Height; } }

        private void Clamp()
        {
            int max = Math.Max(0, _contentHeight - ClientSize.Height);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
            Canvas.Top = -_scroll;
        }

        public void ScrollBy(int dy)
        {
            _scroll += dy;
            Clamp();
            Invalidate();
            Canvas.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int w = ClientSize.Width - (ScrollbarNeeded ? 14 : 0);
            Canvas.SetBounds(0, -_scroll, Math.Max(50, w), Math.Max(ClientSize.Height, _contentHeight));
            Clamp();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!ScrollbarNeeded) return;
            Theme.FillRound(e.Graphics, ThumbRect(), 4, Theme.Scroll);
        }

        private Rectangle ThumbRect()
        {
            int h = ClientSize.Height;
            int thumbH = Math.Max(40, (int)(h * (double)h / Math.Max(1, _contentHeight)));
            int max = Math.Max(1, _contentHeight - h);
            int y = max <= 0 ? 0 : (int)((h - thumbH) * ((double)_scroll / max));
            return new Rectangle(ClientSize.Width - 13, y, 9, thumbH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (ScrollbarNeeded && e.X >= ClientSize.Width - 20)
            {
                // 点在轨道空白处先跳到该位置，再进入拖动，操作起来更接近系统滚动条
                int h = ClientSize.Height;
                int thumbH = Math.Max(40, (int)(h * (double)h / Math.Max(1, _contentHeight)));
                int max = Math.Max(1, _contentHeight - h);
                if (e.Y < ThumbRect().Top || e.Y > ThumbRect().Bottom)
                {
                    int target = (int)((e.Y - thumbH / 2.0) / Math.Max(1, h - thumbH) * max);
                    _scroll = Math.Max(0, Math.Min(target, max));
                    Clamp();
                    Invalidate();
                    Canvas.Invalidate();
                }
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int max = Math.Max(1, _contentHeight - ClientSize.Height);
            int thumbH = Math.Max(40, (int)(ClientSize.Height * (double)ClientSize.Height / Math.Max(1, _contentHeight)));
            int span = Math.Max(1, ClientSize.Height - thumbH);
            int delta = (int)((e.Y - _dragStartY) * (double)max / span);
            _scroll = _dragStartScroll + delta;
            Clamp();
            Invalidate();
            Canvas.Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }
    }

    public class VRow
    {
        public string Title = "";
        public string Sub = "";
        /// <summary>跟在说明文字后面的可点击蓝色链接（例如“点击查看详情”），点行任意位置会打开详情。</summary>
        public string SubLink = "";
        public string Note = "";
        public Color SubColor = Theme.Muted;
        public string Right = "";
        public string Tag = "";
        public string Action = "";
        public Color ActionColor = Theme.Muted;
        public Color TagColor = Theme.Muted;
        public bool Checkable = true;
        public bool Checked = false;
        public bool Disabled = false;
        public bool Alarm = false;
        /// <summary>高风险项：标题、标签与右侧数字统一用警示色，方便一眼挑出来。</summary>
        public bool Risk = false;
        public double Fraction = -1;
        public Color BarColor = Theme.Accent;
        public object Data;
        public object Data2;
        public bool Visible = true;
    }

    /// <summary>虚拟化自绘列表，用于清理结果、应用列表、磁盘目录等。</summary>
    public class VList : Control
    {
        /// <summary>右侧数字/状态列的固定宽度，绘制与表头共用，避免列串位。</summary>
        public const int RightW = 132;

        private List<VRow> _rows = new List<VRow>();
        private List<VRow> _view = new List<VRow>();
        public int RowHeight = 64;
        public bool ShowCheck = true;
        public string RightHeader = "";
        public string EmptyText = Lang.T("暂无内容");
        public string EmptyHint = "";
        public bool NormalizeTags = false;

        private int _scroll;
        private int _hover = -1;
        private int _selected = -1;
        private bool _anyAction = false;
        private bool _sbDrag = false;
        private int _sbDragY = 0;
        private int _sbDragScroll = 0;

        // 加载态：列表还空着在等数据时，画一个居中的转圈和提示，避免让人以为程序停了
        private bool _loading = false;
        private string _loadingText = "";
        private string _loadingHint = "";
        private double _spin = 0;
        private Timer _spinTimer;

        public event EventHandler CheckChanged;
        public event EventHandler RowActivated;
        public event EventHandler ActionClicked;

        public VList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            _spinTimer = new Timer();
            _spinTimer.Interval = 60;
            _spinTimer.Tick += delegate { _spin += 12; if (_spin >= 360) _spin -= 360; if (_loading) Invalidate(); };
        }

        /// <summary>进入/退出加载态。on=true 时列表不再显示“暂无内容”，改画居中的转圈与提示文字。</summary>
        public void SetLoading(bool on, string text)
        {
            SetLoading(on, text, "");
        }

        public void SetLoading(bool on, string text, string hint)
        {
            _loading = on;
            if (text != null) _loadingText = text;
            if (hint != null) _loadingHint = hint;
            if (_loading) { if (!_spinTimer.Enabled) _spinTimer.Start(); }
            else _spinTimer.Stop();
            Invalidate();
        }

        public bool Loading { get { return _loading; } }

        public List<VRow> Rows { get { return _rows; } }

        public int SelectedIndex { get { return _selected; } }
        public VRow SelectedRow
        {
            get
            {
                if (_selected >= 0 && _selected < _view.Count) return _view[_selected];
                return null;
            }
        }

        public void SetRows(List<VRow> rows)
        {
            _rows = rows;
            Rebuild();
        }

        public void Clear()
        {
            _rows.Clear();
            Rebuild();
        }

        public void Rebuild()
        {
            _view.Clear();
            _anyAction = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Visible)
                {
                    _view.Add(_rows[i]);
                    if (_rows[i].Action.Length > 0) _anyAction = true;
                }
            }
            _scroll = 0;
            _selected = -1;
            Invalidate();
        }

        public int VisibleCount { get { return _view.Count; } }
        public VRow RowAt(int i) { return _view[i]; }

        public void SetScroll(int v)
        {
            int max = Math.Max(0, _view.Count * RowHeight - (ClientSize.Height - 8));
            _scroll = Math.Max(0, Math.Min(v, max));
            Invalidate();
        }

        public void ScrollBy(int dy)
        {
            SetScroll(_scroll + dy);
        }

        public void CheckAll(bool on)
        {
            for (int i = 0; i < _view.Count; i++)
            {
                if (_view[i].Checkable) _view[i].Checked = on;
            }
            Invalidate();
            if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
        }

        public void InvertChecks()
        {
            for (int i = 0; i < _view.Count; i++)
            {
                if (_view[i].Checkable) _view[i].Checked = !_view[i].Checked;
            }
            Invalidate();
            if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
        }

        private int HeaderHeight { get { return RightHeader.Length > 0 ? 30 : 4; } }

        // 高度按整行对齐：列表底部不会出现被切掉一半的行
        private bool _snapHeight;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_snapHeight) return;
            int rowH = Math.Max(24, RowHeight);
            int body = Height - HeaderHeight;
            if (body > rowH)
            {
                int extra = body % rowH;
                if (extra > 0)
                {
                    _snapHeight = true;
                    Height = Height - extra;
                    _snapHeight = false;
                }
            }
        }

        /// <summary>滚动条宽度与可点区域：条本身 10px，连同留白 22px 都可抓，免得点不中。</summary>
        public const int ScrollBarW = 10;
        private const int ScrollHitW = 22;

        private int TotalHeight { get { return _view.Count * RowHeight + HeaderHeight; } }
        private bool HasScrollBar { get { return TotalHeight > Height; } }

        private Rectangle ThumbRect()
        {
            int total = TotalHeight;
            if (total <= Height) return Rectangle.Empty;
            int thumbH = Math.Max(48, (int)(Height * (double)Height / total));
            int max = Math.Max(1, total - Height);
            int y = (int)((Height - thumbH) * ((double)_scroll / max));
            return new Rectangle(Width - ScrollBarW - 6, y, ScrollBarW, thumbH);
        }

        private void DragTo(int y)
        {
            int total = TotalHeight;
            if (total <= Height) return;
            Rectangle th = ThumbRect();
            int span = Math.Max(1, Height - th.Height);
            int max = Math.Max(1, total - Height);
            int delta = (int)((y - _sbDragY) * (double)max / span);
            SetScroll(_sbDragScroll + delta);
        }

        private int IndexAt(int y)
        {
            int i = (y + _scroll - HeaderHeight) / RowHeight;
            if (y < HeaderHeight) return -1;
            if (i < 0 || i >= _view.Count) return -1;
            return i;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta / 120 * (RowHeight + 8));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_sbDrag)
            {
                DragTo(e.Y);
                return;
            }
            bool overBar = HasScrollBar && e.X >= Width - ScrollHitW;
            Cursor = overBar ? Cursors.Hand : Cursors.Default;
            int idx = IndexAt(e.Y);
            if (idx != _hover)
            {
                _hover = idx;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _sbDrag = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (HasScrollBar && e.X >= Width - ScrollHitW)
            {
                // 轨道空白处点击先跳到该位置，再进入拖动；按在滑块上则直接拖
                Rectangle th = ThumbRect();
                if (e.Y < th.Top || e.Y > th.Bottom)
                {
                    int max = Math.Max(1, TotalHeight - Height);
                    int span = Math.Max(1, Height - th.Height);
                    int target = (int)((e.Y - th.Height / 2.0) / span * max);
                    SetScroll(target);
                }
                _sbDrag = true;
                _sbDragY = e.Y;
                _sbDragScroll = _scroll;
                return;
            }
            int idx = IndexAt(e.Y);
            if (idx < 0) return;
            VRow row = _view[idx];
            _selected = idx;
            int actionX = ActionRect(row, idx).X;
            if (row.Action.Length > 0 && e.X >= actionX - 4)
            {
                if (ActionClicked != null) ActionClicked(this, EventArgs.Empty);
                Invalidate();
                return;
            }
            if (ShowCheck && row.Checkable && e.X <= 52)
            {
                row.Checked = !row.Checked;
                Invalidate();
                if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
                return;
            }
            Invalidate();
            if (RowActivated != null) RowActivated(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int idx = IndexAt(e.Y);
            if (idx < 0) return;
            _selected = idx;
            if (RowActivated != null) RowActivated(this, EventArgs.Empty);
        }

        private int ActionWidth(VRow row)
        {
            return Math.Min(100, Math.Max(62, TextRenderer.MeasureText(row.Action, Theme.F(9.5f)).Width + 18));
        }

        private Rectangle ActionRect(VRow row, int idx)
        {
            if (row.Action.Length <= 0) return Rectangle.Empty;
            int w = ActionWidth(row);
            int top = HeaderHeight + idx * RowHeight - _scroll;
            return new Rectangle(Width - w - 26, top + (RowHeight - 28) / 2, w, 28);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            if (RightHeader.Length > 0)
            {
                using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
                {
                    Theme.Txt(g, Lang.T("项目"), Theme.F(9.5f), Theme.Muted, new Rectangle(20, 6, 200, 20), sf);
                }
                using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
                {
                    int hw = RightW;
                    int ax = _anyAction ? 90 : 0;
                    Theme.Txt(g, RightHeader, Theme.F(9.5f), Theme.Muted, new Rectangle(Width - hw - ax - 14, 6, hw - 6, 20), sf);
                }
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 4, HeaderHeight - 8, Width - 4, HeaderHeight - 8);
                }
            }

            if (_loading)
            {
                // 与清理页的进度圆环同尺寸：同样的直径算法、同样的描边粗细，各页看起来一致
                int d = Math.Min(Math.Min(Width - 160, Height - 190), 200);
                if (d < 104) d = 104;
                int cy = Math.Max(8, Height / 2 - d / 2 - 46);
                Rectangle ringRect = new Rectangle(Width / 2 - d / 2, cy, d, d);
                int pen = Math.Max(10, d / 14);
                using (Pen p = new Pen(Theme.Track, pen))
                {
                    g.DrawEllipse(p, ringRect);
                }
                using (Pen p = new Pen(Theme.Accent, pen))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawArc(p, ringRect, (float)(-90 + _spin), 90f);
                }
                using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Near))
                {
                    Theme.Txt(g, _loadingText.Length == 0 ? Lang.T("正在加载...") : _loadingText, Theme.F(10f), Theme.TextDim,
                              new Rectangle(0, ringRect.Bottom + 18, Width, 26), sf);
                    if (_loadingHint.Length > 0)
                        Theme.Txt(g, _loadingHint, Theme.F(8.5f), Theme.Hint,
                                  new Rectangle(0, ringRect.Bottom + 50, Width, 22), sf);
                }
                return;
            }

            if (_view.Count == 0)
            {
                using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
                {
                    Theme.Txt(g, EmptyText, Theme.F(10f), Theme.Muted, new Rectangle(0, Height / 2 - 30, Width, 24), sf);
                    if (EmptyHint.Length > 0)
                        Theme.Txt(g, EmptyHint, Theme.F(8.5f), Theme.Hint, new Rectangle(0, Height / 2 - 2, Width, 24), sf);
                }
                return;
            }

            // 行区域严格裁到表头下方，滚动时行不会爬到表头文字上
            GraphicsState gs = g.Save();
            g.SetClip(new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));
            int first = Math.Max(0, _scroll / RowHeight);
            int last = Math.Min(_view.Count - 1, (_scroll + Height) / RowHeight + 1);
            for (int i = first; i <= last; i++)
            {
                DrawRow(g, _view[i], i);
            }
            g.Restore(gs);

            int total = _view.Count * RowHeight + HeaderHeight;
            if (total > Height)
            {
                Theme.FillRound(g, ThumbRect(), ScrollBarW / 2, Theme.Scroll);
            }
        }

        private void DrawRow(Graphics g, VRow row, int idx)
        {
            int top = HeaderHeight + idx * RowHeight - _scroll;
            Rectangle r = new Rectangle(4, top + 2, Width - 12, RowHeight - 4);

            if (idx == _hover) Theme.FillRound(g, r, 9, Theme.CardSoft);
            else if (idx == _selected) Theme.FillRound(g, r, 9, Theme.Card);

            int textX = 20;
            if (ShowCheck)
            {
                Rectangle box = new Rectangle(22, top + (RowHeight - 16) / 2, 16, 16);
                if (row.Checkable)
                {
                    Theme.FillRound(g, box, 5, row.Checked ? Theme.Accent : Theme.CheckBg);
                    if (row.Checked)
                    {
                        using (Pen p = new Pen(Theme.CheckMark, 2f))
                        {
                            g.DrawLine(p, box.X + 4, box.Y + 8, box.X + 7, box.Y + 11);
                            g.DrawLine(p, box.X + 7, box.Y + 11, box.X + 12, box.Y + 4);
                        }
                    }
                    else
                    {
                        Theme.StrokeRound(g, box, 5, Theme.Border, 1);
                    }
                }
                textX = 50;
            }

            // 右侧“体积/状态”列与操作按钮列宽固定，标题、标签、数字互相避让，不再叠字
            int rightW = RightW;
            int actionW = row.Action.Length > 0 ? ActionWidth(row) + 4 : 0;
            int rightX = Width - rightW - actionW - 20;

            Color titleColor = row.Disabled ? Theme.Muted : (row.Risk ? Theme.Danger : Theme.Text);
            int textW = Math.Max(40, rightX - textX - 34);
            using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
            {
                Theme.Txt(g, row.Title, Theme.F(10.5f), titleColor, new Rectangle(textX, top + 8, textW, 24), sf);
            }
            if (row.Sub.Length > 0)
            {
                using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
                {
                    Font subFont = Theme.F(9.5f);
                    int linkW = 0;
                    if (row.SubLink.Length > 0)
                    {
                        // 用 GDI+ 测量（与 DrawString 同源），GDI 的 TextRenderer 在这里会多算一截，导致尾部被省略号吃掉
                        linkW = (int)Math.Ceiling(g.MeasureString(row.SubLink, subFont).Width) + 8;
                    }
                    int avail = Math.Max(40, textW - linkW);
                    int used = (int)Math.Ceiling(g.MeasureString(row.Sub, subFont).Width) + 2;
                    int subW = Math.Min(used, avail);
                    Theme.Txt(g, row.Sub, subFont, row.SubColor, new Rectangle(textX, top + 34, subW, 20), sf);
                    if (linkW > 0)
                    {
                        Theme.Txt(g, row.SubLink, subFont, Theme.Blue, new Rectangle(textX + subW + 2, top + 34, linkW, 20), sf);
                    }
                }
            }
            if (row.Note.Length > 0)
            {
                using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
                {
                    Theme.Txt(g, row.Note, Theme.F(9.5f), Theme.Muted, new Rectangle(textX, top + 58, textW, 20), sf);
                }
            }

            if (row.Fraction >= 0)
            {
                int barY = top + RowHeight - 9;
                int barW = textW;
                if (barW > 40)
                {
                    Theme.FillRound(g, new Rectangle(textX, barY, barW, 4), 2, Theme.Track);
                    int fill = (int)(barW * Math.Max(0.02, Math.Min(1.0, row.Fraction)));
                    Theme.FillRound(g, new Rectangle(textX, barY, fill, 4), 2, row.BarColor);
                }
            }

            if (row.Tag.Length > 0)
            {
                Font tf = Theme.F(9f);
                Color tagColor = row.Risk ? Theme.Danger : row.TagColor;
                int tw = Math.Min(rightX - textX - 120, (int)g.MeasureString(row.Tag, tf).Width + 14);
                Rectangle tagRect = new Rectangle(rightX - tw, top + (RowHeight - 22) / 2, tw, 22);
                Theme.FillRound(g, tagRect, 4, Theme.Alpha(tagColor, row.Risk ? 34 : 26));
                using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
                {
                    Theme.Txt(g, row.Tag, tf, tagColor, tagRect, sf);
                }
            }

            if (row.Action.Length > 0)
            {
                Rectangle ar = ActionRect(row, idx);
                Theme.FillRound(g, ar, 6, Theme.Alpha(row.ActionColor, 22));
                Theme.StrokeRound(g, ar, 6, Theme.Alpha(row.ActionColor, 60), 1);
                using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
                {
                    Theme.Txt(g, row.Action, Theme.F(9.5f), row.ActionColor, ar, sf);
                }
            }

            using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Center))
            {
                Theme.Txt(g, row.Right, Theme.F(9.8f), row.Risk ? Theme.Danger : (row.Alarm ? Theme.Warn : Theme.TextDim), new Rectangle(rightX, top, rightW - 6, RowHeight), sf);
            }
        }
    }

    /// <summary>三档分段选择器：系统 / 浅色 / 深色。</summary>
    public class Seg3 : Control
    {
        // 原始中文文案；绘制时按当前语言实时取词，避免切换语言后仍是旧文字
        public static readonly string[] RawLabels = new string[] { "系统", "浅色", "深色" };
        public string[] Labels = new string[] { Lang.T("系统"), Lang.T("浅色"), Lang.T("深色") };
        public string[] Modes = new string[] { "system", "light", "dark" };
        public string Mode = "dark";
        private int _hover = -1;
        public event EventHandler Changed;

        public Seg3()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.TitleBg;
            Font = Theme.F(9.5f);
            Size = new Size(196, 28);
            Cursor = Cursors.Hand;
        }

        /// <summary>按当前语言文字实测宽度撑开自身宽度，俄/德等长词不再挤在一起。</summary>
        public void FitWidth()
        {
            int total = 0;
            using (Graphics g = CreateGraphics())
            {
                for (int i = 0; i < 3; i++)
                {
                    int w = (int)Math.Ceiling(g.MeasureString(Lang.T(RawLabels[i]), Font).Width) + 28;
                    if (w < 36) w = 36;
                    total += w;
                }
            }
            Width = Math.Max(196, total + 4);
        }

        /// <summary>按每段文字的实测宽度分配段宽，避免小语种被截断。</summary>
        private int[] SegWidths()
        {
            int[] need = new int[3];
            int total = 0;
            using (Graphics g = CreateGraphics())
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                for (int i = 0; i < 3; i++)
                {
                    need[i] = (int)Math.Ceiling(g.MeasureString(Lang.T(RawLabels[i]), Font).Width) + 26;
                    if (need[i] < 36) need[i] = 36;
                    total += need[i];
                }
            }
            int avail = Width - 4;
            if (avail < total)
            {
                int[] squeezed = new int[3];
                for (int i = 0; i < 3; i++) squeezed[i] = (int)((long)need[i] * avail / total);
                int fix = avail - (squeezed[0] + squeezed[1] + squeezed[2]);
                if (fix != 0) squeezed[2] += fix;
                return squeezed;
            }
            int extra = avail - total;
            int[] widths = new int[3];
            for (int i = 0; i < 3; i++) widths[i] = need[i] + extra / 3;
            widths[2] += extra - (extra / 3) * 3;
            return widths;
        }

        private int IndexAt(int x)
        {
            int[] ws = SegWidths();
            int left = 2;
            for (int i = 0; i < 3; i++)
            {
                if (x >= left && x < left + ws[i]) return i;
                left += ws[i];
            }
            return x < 2 ? 0 : 2;
        }

        private int ActiveIndex()
        {
            for (int i = 0; i < Modes.Length; i++) if (Modes[i] == Mode) return i;
            return 2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.X);
            if (i != _hover) { _hover = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.X);
            if (i < 0) return;
            Mode = Modes[i];
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            bool onBlue = Theme.TitleOnBlue;
            Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 7, onBlue ? Theme.Alpha(Color.White, 36) : Theme.CardSoft);
            Theme.StrokeRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 7, onBlue ? Theme.Alpha(Color.White, 70) : Theme.BorderSoft, 1);
            int[] ws = SegWidths();
            int act = ActiveIndex();
            int x = 2;
            for (int i = 0; i < 3; i++)
            {
                Rectangle seg = new Rectangle(x, 2, ws[i], Height - 4);
                x += ws[i];
                if (i == act) Theme.FillRound(g, seg, 5, onBlue ? Color.White : Theme.Accent);
                else if (i == _hover) Theme.FillRound(g, seg, 5, onBlue ? Theme.Alpha(Color.White, 34) : Theme.CardHover);
                Color fg = i == act
                    ? (onBlue ? Theme.TitleBg : Theme.AccentFg)
                    : (onBlue ? Theme.TitleText : Theme.TextDim);
                using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
                {
                    Theme.Txt(g, Lang.T(RawLabels[i]), Font, fg, seg, sf);
                }
            }
        }
    }

    /// <summary>带文字标签的勾选框，用于“隐藏已禁用”等开关项。</summary>
    public class CheckBox2 : Control
    {
        public bool Checked = false;
        public new string Text = "";
        private bool _hover;
        public event EventHandler Changed;

        public CheckBox2()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Font = Theme.F(8.2f);
            Cursor = Cursors.Hand;
            Size = new Size(120, 32);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Checked = !Checked;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            Rectangle box = new Rectangle(0, (Height - 16) / 2, 16, 16);
            if (Checked)
            {
                Theme.FillRound(g, box, 5, Theme.Accent);
                using (Pen p = new Pen(Theme.CheckMark, 2f))
                {
                    g.DrawLine(p, box.X + 4, box.Y + 8, box.X + 7, box.Y + 11);
                    g.DrawLine(p, box.X + 7, box.Y + 11, box.X + 12, box.Y + 4);
                }
            }
            else
            {
                Theme.FillRound(g, box, 5, _hover ? Theme.CardHover : Theme.CheckBg);
                Theme.StrokeRound(g, box, 5, _hover ? Theme.Accent : Theme.Border, 1);
            }
            using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Center))
            {
                Theme.Txt(g, Text, Font, _hover ? Theme.Text : Theme.TextDim,
                    new Rectangle(22, 0, Width - 24, Height), sf);
            }
        }
    }

    /// <summary>开关控件。</summary>
    public class Toggle : Control
    {
        public bool Checked = false;
        private bool _hover;
        public event EventHandler Changed;

        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            Size = new Size(46, 24);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Checked = !Checked;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, Height / 2, Checked ? Theme.Alpha(Theme.Accent, 210) : Theme.Border);
            int k = Height - 8;
            int x = Checked ? Width - k - 4 : 4;
            Theme.FillRound(e.Graphics, new Rectangle(x, 4, k, k), k / 2, Checked ? Theme.AccentFg : Theme.TextDim);
        }
    }

    /// <summary>搜索框（自绘，带原生占位提示）。</summary>
    public class SearchBox : Control
    {
        public TextBox Input;
        private string _placeholder = Lang.T("搜索...");

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            Height = 34;
            Input = new TextBox();
            Input.BorderStyle = BorderStyle.None;
            Input.BackColor = Theme.Card;
            Input.ForeColor = Theme.Text;
            Input.Font = Theme.F(9.8f);
            Input.SetBounds(34, 9, 100, 18);
            Input.TextChanged += delegate { Invalidate(); };
            Controls.Add(Input);
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value;
                ApplyCue();
                Invalidate();
            }
        }

        public string Text2 { get { return Input.Text; } set { Input.Text = value; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCue();
        }

        private void ApplyCue()
        {
            try
            {
                if (Input != null && Input.IsHandleCreated)
                {
                    SendMessage(Input.Handle, 0x1501, (IntPtr)1, _placeholder);
                }
            }
            catch (Exception) { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Input == null) return;
            Input.SetBounds(34, (Height - Input.Height) / 2, Math.Max(20, Width - 50), Input.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, 8, Theme.Card);
            Theme.StrokeRound(e.Graphics, r, 8, Theme.Border, 1);
            using (Pen p = new Pen(Theme.Muted, 1.4f))
            {
                e.Graphics.DrawEllipse(p, 12, 11, 9, 9);
                e.Graphics.DrawLine(p, 20, 20, 24, 24);
            }
        }
    }

    /// <summary>健康评分圆环。</summary>
    public class Ring : Control
    {
        public int Score = 0;
        public string Caption = Lang.T("健康评分");
        private double _anim = 0;

        public Ring()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
        }

        public void SetScore(int score)
        {
            Score = score;
            _anim = 0;
            Invalidate();
        }

        public void Tick()
        {
            if (Math.Abs(_anim - Score) < 1) { _anim = Score; return; }
            _anim += (Score - _anim) * 0.22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            int size = Math.Min(Width, Height) - 16;
            Rectangle r = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using (Pen track = new Pen(Theme.Track, 9f))
            {
                g.DrawArc(track, r, -90, 360);
            }
            Color c = Score >= 80 ? Theme.Accent : (Score >= 60 ? Theme.Warn : Theme.Danger);
            using (Pen arc = new Pen(c, 9f))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, r, -90, (float)(360 * Math.Max(0, Math.Min(100, _anim)) / 100));
            }
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                Theme.Txt(g, ((int)_anim).ToString(), Theme.F(22f, FontStyle.Bold), Theme.Text, new Rectangle(r.X, r.Y + size / 2 - 34, size, 40), sf);
                Theme.Txt(g, Caption, Theme.F(8f), Theme.Muted, new Rectangle(r.X, r.Y + size / 2 + 10, size, 20), sf);
            }
        }
    }

    /// <summary>细进度条。</summary>
    public class MiniBar : Control
    {
        public double Value = 0;
        public Color BarColor = Theme.Accent;
        public int BarHeight = 5;

        public MiniBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            Height = 5;
        }

        public void SetValue(double v)
        {
            Value = Math.Max(0, Math.Min(1, v));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.FillRound(e.Graphics, new Rectangle(0, (Height - BarHeight) / 2, Width, BarHeight), BarHeight / 2, Theme.Track);
            int w = (int)(Width * Value);
            if (w > 2) Theme.FillRound(e.Graphics, new Rectangle(0, (Height - BarHeight) / 2, w, BarHeight), BarHeight / 2, BarColor);
        }
    }

    /// <summary>带百分比文字的进度条，用于扫描与清理过程。</summary>
    public class ProgressBar2 : Control
    {
        public int Percent = 0;
        public string Caption = "";
        public string RightText = "";
        public Color BarColor = Theme.Accent;

        public ProgressBar2()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 52;
            // 百分数还是 0 的时候画一段转动的弧，避免刚扫描时看起来完全静止
            _pulse = new Timer();
            _pulse.Interval = 70;
            _pulse.Tick += delegate
            {
                _angle += 12;
                if (_angle >= 360) _angle -= 360;
                if (Visible && Percent <= 0) Invalidate();
            };
            _pulse.Start();
        }

        private Timer _pulse;
        private double _angle = 0;

        public void SetProgress(int percent, string caption)
        {
            Percent = Math.Max(0, Math.Min(100, percent));
            Caption = caption == null ? "" : caption;
            RightText = Percent + "%";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            if (Height < 150) { PaintCompact(g); return; }
            PaintCentered(g);
        }

        /// <summary>矮条形态：列表上方的单行卡片，标题左、百分比右、轨道在底部。</summary>
        private void PaintCompact(Graphics g)
        {
            Rectangle panel = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, panel, 8, Theme.Card);
            Theme.StrokeRound(g, panel, 8, Theme.BorderSoft, 1);

            int pad = 14;
            int percentW = 56;
            Font capFont = Theme.F(8.6f);
            Font pctFont = Theme.F(11f, FontStyle.Bold);

            using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
            {
                Theme.Txt(g, Caption.Length == 0 ? Lang.T("准备中...") : Caption, capFont, Theme.TextDim,
                          new Rectangle(pad, 6, Width - pad * 2 - percentW, 18), sf);
            }
            using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
            {
                Theme.Txt(g, RightText, pctFont, Theme.Accent,
                          new Rectangle(Width - pad - percentW, 4, percentW, 20), sf);
            }

            Rectangle track = new Rectangle(pad, Height - 16, Width - pad * 2, 8);
            Theme.FillRound(g, track, 4, Theme.Track);
            int w = (int)(track.Width * Percent / 100.0);
            if (w > 3) Theme.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), 4, BarColor);
        }

        /// <summary>居中形态：圆环与百分比落在区域正中，进度条与说明文字排在其下。</summary>
        private void PaintCentered(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            int d = Math.Min(Math.Min(Width - 160, Height - 190), 200);
            if (d < 104) d = 104;
            int cx = Width / 2;
            int cy = Math.Max(8, Height / 2 - d / 2 - 46);
            Rectangle ring = new Rectangle(cx - d / 2, cy, d, d);
            int pen = Math.Max(10, d / 14);

            using (Pen p = new Pen(Theme.Track, pen))
            {
                g.DrawEllipse(p, ring);
            }
            float sweep = 360f * Percent / 100f;
            if (sweep > 0.4f)
            {
                using (Pen p = new Pen(BarColor, pen))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawArc(p, ring, -90f, sweep);
                }
            }
            else
            {
                using (Pen p = new Pen(Theme.Alpha(BarColor, 170), pen))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawArc(p, ring, (float)(-90 + _angle), 90f);
                }
            }

            float pctSize = d >= 170 ? 26f : (d >= 130 ? 21f : 17f);
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                Theme.Txt(g, Percent + "%", Theme.F(pctSize, FontStyle.Bold), Theme.Accent,
                          new Rectangle(ring.X, ring.Y, ring.Width, ring.Height), sf);
            }

            // 说明文字：环下方一行，过长自动居中省略
            int capW = Math.Min(Width - 60, 620);
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Near))
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                Theme.Txt(g, Caption.Length == 0 ? Lang.T("准备中...") : Caption, Theme.F(10f), Theme.TextDim,
                          new Rectangle((Width - capW) / 2, ring.Bottom + 18, capW, 24), sf);
            }

            // 进度条：环下方，较宽的一条，长度与环径分离，视觉上“环是主角、条是刻度”
            int barW = Math.Min(Width - 120, 460);
            int barH = 10;
            Rectangle track = new Rectangle((Width - barW) / 2, ring.Bottom + 52, barW, barH);
            Theme.FillRound(g, track, 5, Theme.Track);
            int w = (int)(track.Width * Percent / 100.0);
            if (w > 5) Theme.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), 5, BarColor);
        }
    }

    /// <summary>加载指示器。</summary>
    public class Spinner : Control
    {
        private double _angle;
        private Timer _timer;

        public Spinner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            Size = new Size(34, 34);
            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += delegate { _angle += 12; if (_angle >= 360) _angle -= 360; Invalidate(); };
        }

        public void Spin(bool on)
        {
            if (on) _timer.Start(); else _timer.Stop();
            Visible = on;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            int s = Math.Min(Width, Height) - 6;
            Rectangle r = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);
            using (Pen p = new Pen(Theme.Alpha(Theme.Accent, 90), 3f))
            {
                e.Graphics.DrawArc(p, r, (float)_angle, 110);
            }
            using (Pen p = new Pen(Theme.Accent, 3f))
            {
                e.Graphics.DrawArc(p, r, (float)_angle + 200, 40);
            }
        }
    }

    /// <summary>页面底部的操作条。</summary>
    public class ActionBar : Panel2
    {
        public ActionBar()
        {
            BackColor = Theme.Bg;
            Height = 58;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.CardAlt))
            {
                e.Graphics.FillRectangle(b, 0, 0, Width, Height);
            }
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, 0, 0, Width, 0);
            }
            base.OnPaint(e);
        }
    }
}

