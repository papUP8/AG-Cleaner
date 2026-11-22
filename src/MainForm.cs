using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    /// <summary>页面基类：统一标题区与内容区。</summary>
    public abstract class PageBase : Panel2
    {
        public abstract string PageTitle { get; }
        public abstract string PageSubtitle { get; }
        public const int HeaderH = 82;

        public virtual void OnEnter() { }
        public virtual void Tick() { }

        protected void DrawHeader(Graphics g)
        {
            Theme.Txt(g, PageTitle, Theme.F(19f, FontStyle.Bold), Theme.Text, 2, 6);
            Theme.Txt(g, PageSubtitle, Theme.F(8.8f), Theme.Muted, 3, 44);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            using (SolidBrush b = new SolidBrush(Theme.Bg))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
            DrawHeader(e.Graphics);
            base.OnPaint(e);
        }
    }

    public class MainForm : Form
    {
        public const int SidebarW = 212;
        public const int TitleH = 56;

        private Panel2 _title;
        private Panel2 _sidebar;
        private Panel _content;
        private List<NavItem> _navs = new List<NavItem>();
        private List<PageBase> _pages = new List<PageBase>();
        private int _current = 0;
        public static MainForm Instance = null;
        private Timer _tick;
        private Label _verLabel;
        private Label _credit;
        private Seg3 _themeSeg;
        private TitleBtn _langBtn;
        private Timer _sysWatch;
        private string _themeMode = "dark";

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public MainForm()
        {
            Instance = this;
            Text = Lang.T("AG Cleaner 桌面清理");
            Icon = Program.AppIcon();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1200, 788);
            MinimumSize = new Size(1040, 700);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Font = Theme.F(9f);
            KeyPreview = true;

            BuildTitle();
            BuildSidebar();
            BuildPages();

            _tick = new Timer();
            _tick.Interval = 1200;
            _tick.Tick += delegate
            {
                PageBase p = _pages[_current];
                p.Tick();
            };
            _tick.Start();
            WatchSystemTheme();

            SelectPage(0);
        }

        public void SelectPage(int index)
        {            if (index < 0 || index >= _pages.Count) return;
            _current = index;
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;
            for (int i = 0; i < _navs.Count; i++)
            {
                _navs[i].Active = i == index;
                _navs[i].Invalidate();
            }
            _pages[index].Visible = true;
            _pages[index].BringToFront();
            _pages[index].OnEnter();
        }

        public PageBase PageAt(int index)
        {
            if (index < 0 || index >= _pages.Count) return null;
            return _pages[index];
        }

        private void BuildTitle()
        {
            _title = new Panel2();
            _title.SetBounds(0, 0, ClientSize.Width, TitleH);
            _title.BackColor = Theme.TitleBg;
            _title.Paint += TitlePaint;
            _title.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
                }
            };
            Controls.Add(_title);

            MiniBtn min = new MiniBtn();
            min.Glyph = "—";
            min.SetBounds(ClientSize.Width - 92, 16, 36, 24);
            min.Click += delegate { WindowState = FormWindowState.Minimized; };
            _title.Controls.Add(min);

            MiniBtn close = new MiniBtn();
            close.Glyph = "✕";
            close.Danger = true;
            close.SetBounds(ClientSize.Width - 48, 16, 36, 24);
            close.Click += delegate { Close(); };
            _title.Controls.Add(close);

            _themeSeg = new Seg3();
            Settings startup = Settings.Load();
            _themeMode = startup.ThemeMode;
            if (_themeMode != "light" && _themeMode != "dark" && _themeMode != "system") _themeMode = "dark";
            _themeSeg.Mode = _themeMode;
            _themeSeg.SetBounds(ClientSize.Width - 286, 16, 176, 24);
            _themeSeg.Changed += delegate { SetThemeMode(_themeSeg.Mode); };
            _title.Controls.Add(_themeSeg);

            _langBtn = new TitleBtn();
            _langBtn.Size = new Size(58, 24);
            _langBtn.Click += delegate { OpenLanguageMenu(); };
            _langBtn.SetBounds(ClientSize.Width - 372, 16, 68, 24);
            _title.Controls.Add(_langBtn);
            SyncLanguageButton();
        }

        /// <summary>顶栏语言按钮：显示当前语言代码，点击弹出六种语言的菜单。</summary>
        private void SyncLanguageButton()
        {
            if (_langBtn == null) return;
            _langBtn.Text = Lang.Badge(Lang.Cur);
            _langBtn.Invalidate();
        }

        private void OpenLanguageMenu()
        {
            if (!LangSwitch.Pick(this)) return;
            SyncLanguageButton();
            // 点击来源就在即将被重建的页面上，延后一帧再重建，避免在事件里 Dispose 自己
            BeginInvoke((Action)delegate { ReloadPages(_current); });
        }

        private void SetThemeMode(string mode)
        {
            _themeMode = mode;
            Theme.ApplyMode(mode, Theme.SystemLight());
            Settings st = Settings.Load();
            st.ThemeMode = mode;
            st.Save();
            ApplyTheme();
            if (_themeSeg != null) { _themeSeg.Mode = mode; _themeSeg.Invalidate(); }
        }

        // 跟随系统时，Windows 切换深浅色后自动跟着变
        private void WatchSystemTheme()
        {
            _sysWatch = new Timer();
            _sysWatch.Interval = 3000;
            _sysWatch.Tick += delegate
            {
                if (_themeMode != "system") return;
                bool light = Theme.SystemLight();
                Theme.Skin skin = light ? Theme.Skin.SystemLight : Theme.Skin.SystemDark;
                if (Theme.IsLight == light && Theme.TitleOnBlue) return;
                Theme.ApplySkin(skin);
                ApplyTheme();
            };
            _sysWatch.Start();
        }

        /// <summary>仅供截图/诊断：让顶栏分段按钮与当前模式一致，不写入配置。</summary>
        public void SyncThemeVisualMode(string mode)
        {
            if (_themeSeg == null) return;
            _themeMode = mode;
            _themeSeg.Mode = mode;
            _themeSeg.Invalidate();
        }

        /// <summary>仅供截图/诊断：让顶栏分段按钮与当前配色一致，不写入配置。</summary>
        public void SyncThemeVisual(bool light)
        {
            if (_themeSeg == null) return;
            Theme.Apply(light);
            _themeMode = light ? "light" : "dark";
            _themeSeg.Mode = _themeMode;
            _themeSeg.Invalidate();
        }

        /// <summary>切换配色后统一刷新各控件的底色与文字色。</summary>
        public void ApplyTheme()
        {
            BackColor = Theme.Bg;
            _title.BackColor = Theme.TitleBg;
            _sidebar.BackColor = Theme.SidebarBg;
            _content.BackColor = Theme.Bg;
            ThemeSync.Apply(_title);
            ThemeSync.Apply(_sidebar);
            ThemeSync.Apply(_content);
            _title.Invalidate();
            _sidebar.Invalidate();
            if (_langBtn != null) _langBtn.Invalidate();
            Invalidate(true);
            for (int i = 0; i < _pages.Count; i++) _pages[i].Invalidate(true);
        }

        private void TitlePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.TitleBg))
            {
                g.FillRectangle(b, 0, 0, _title.Width, _title.Height);
            }
            using (Pen p = new Pen(Theme.TitleOnBlue ? Theme.Alpha(Theme.TitleText, 38) : Theme.Border))
            {
                g.DrawLine(p, 0, TitleH - 1, _title.Width, TitleH - 1);
            }
            // 标识
            Rectangle logo = new Rectangle(18, 15, 26, 26);
            Theme.FillRound(g, logo, 8, Theme.Accent);
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                Theme.Txt(g, "AG", Theme.F(8.2f, FontStyle.Bold), Theme.AccentFg, logo, sf);
            }
            Theme.Txt(g, "AG Cleaner", Theme.F(12f, FontStyle.Bold), Theme.TitleText, 54, 14);
            Theme.Txt(g, Lang.T("桌面清理与优化 · Windows"), Theme.F(8f), Theme.TitleDim, 54, 34);
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel2();
            _sidebar.SetBounds(0, TitleH, SidebarW, ClientSize.Height - TitleH);
            _sidebar.BackColor = Theme.SidebarBg;
            _sidebar.Paint += delegate (object s, PaintEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.SidebarBg))
                {
                    e.Graphics.FillRectangle(b, 0, 0, _sidebar.Width, _sidebar.Height);
                }
                using (Pen p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
                }
            };
            Controls.Add(_sidebar);

            string[] names = new string[] { Lang.T("深度清理"), Lang.T("应用卸载"), Lang.T("启动项管理"), Lang.T("磁盘分析"), Lang.T("系统优化"), Lang.T("设置") };
            string[] glyphs = new string[] { "◉", "✦", "⌫", "▣", "⚡", "⚙", "↺" };

            for (int i = 0; i < names.Length; i++)
            {
                NavItem n = new NavItem();
                n.Text = names[i];
                n.Glyph = glyphs[i];
                n.Font = Theme.F(10.5f);
                n.SetBounds(12, 22 + i * 56, SidebarW - 24, 52);
                int idx = i;
                n.Click += delegate { SelectPage(idx); };
                _sidebar.Controls.Add(n);
                _navs.Add(n);
            }

            _verLabel = new Label();
            _verLabel.AutoSize = false;
            _verLabel.BackColor = Theme.SidebarBg;
            _verLabel.ForeColor = Theme.Muted;
            _verLabel.Font = Theme.F(8f);
            _verLabel.TextAlign = ContentAlignment.MiddleLeft;
            _verLabel.Text = "v" + Program.Version + "  · GPL-3.0";
            _verLabel.SetBounds(20, ClientSize.Height - TitleH - 62, SidebarW - 40, 20);
            _sidebar.Controls.Add(_verLabel);

            Label credit = new Label();
            _credit = credit;
            credit.AutoSize = false;
            credit.BackColor = Theme.SidebarBg;
            credit.ForeColor = Theme.Muted;
            credit.Font = Theme.F(7.5f);
            credit.TextAlign = ContentAlignment.MiddleLeft;
            credit.Text = Lang.T("社区版 · GPL-3.0 · 代码开源");
            credit.SetBounds(20, ClientSize.Height - TitleH - 40, SidebarW - 24, 20);
            _sidebar.Controls.Add(credit);
        }

        private void BuildPages()
        {
            _content = new Panel();
            _content.SetBounds(SidebarW + 1, TitleH, ClientSize.Width - SidebarW - 1, ClientSize.Height - TitleH);
            _content.BackColor = Theme.Bg;
            Controls.Add(_content);
            _content.BringToFront();

            PageBase[] pages = new PageBase[]
            {
                new CleanPage(),
                new UninstallPage(),
                new StartupPage(),
                new AnalyzePage(),
                new OptimizePage(),
                new SettingsPage()
            };
            AddPages(pages);
        }

        private PageBase[] CreatePages()
        {
            return new PageBase[]
            {
                new CleanPage(),
                new UninstallPage(),
                new StartupPage(),
                new AnalyzePage(),
                new OptimizePage(),
                new SettingsPage()
            };
        }

        private void AddPages(PageBase[] pages)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].SetBounds(28, 18, _content.Width - 56, _content.Height - 36);
                pages[i].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                _content.Controls.Add(pages[i]);
                pages[i].Visible = false;
                _pages.Add(pages[i]);
            }
        }

        /// <summary>切换界面语言后按新文案重建全部页面（页面内的扫描结果会清空）。</summary>
        public void ReloadPages(int gotoIndex)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _content.Controls.Remove(_pages[i]);
                _pages[i].Dispose();
            }
            _pages.Clear();
            Instance = this;
            Text = Lang.T("AG Cleaner 桌面清理");
            RetranslateChrome();
            AddPages(CreatePages());
            SelectPage(gotoIndex < 0 || gotoIndex >= _pages.Count ? 0 : gotoIndex);
        }

        private void RetranslateChrome()
        {
            string[] keys = new string[] { "深度清理", "应用卸载", "启动项管理", "磁盘分析", "系统优化", "设置" };
            for (int i = 0; i < _navs.Count && i < keys.Length; i++)
            {
                _navs[i].Text = Lang.T(keys[i]);
                _navs[i].Invalidate();
            }
            if (_credit != null) _credit.Text = Lang.T("社区版 · GPL-3.0 · 代码开源");
            if (_verLabel != null) _verLabel.Text = "v" + Program.Version + "  · GPL-3.0";
            SyncLanguageButton();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_title == null) return;
            _title.SetBounds(0, 0, ClientSize.Width, TitleH);
            _sidebar.SetBounds(0, TitleH, SidebarW, ClientSize.Height - TitleH);
            _content.SetBounds(SidebarW + 1, TitleH, ClientSize.Width - SidebarW - 1, ClientSize.Height - TitleH);
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].SetBounds(28, 18, _content.Width - 56, _content.Height - 36);
            }
            if (_themeSeg != null) _themeSeg.SetBounds(ClientSize.Width - 250, 16, 150, 24);
            if (_langBtn != null) _langBtn.SetBounds(ClientSize.Width - 372, 16, 68, 24);
            _title.Invalidate();
        }

        private const int WM_NCHITTEST = 0x0084;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST)
            {
                Point p = PointToClient(Cursor.Position);
                const int edge = 5;
                int result = 0;
                if (p.Y <= edge) result = 12;
                else if (p.Y >= ClientSize.Height - edge) result = 15;
                else if (p.X <= edge) result = 10;
                else if (p.X >= ClientSize.Width - edge) result = 11;
                if (result != 0) m.Result = (IntPtr)result;
            }
        }

        /// <summary>离屏渲染，用于自检视觉。</summary>
        public void ExportShot(string path)
        {
            if (_pages.Count > _current) _pages[_current].Tick();
            Application.DoEvents();
            Bitmap bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
        }
    }

    /// <summary>标题栏小按钮。</summary>
    public class MiniBtn : Control
    {
        public string Glyph = "";
        public bool Danger = false;
        private bool _hover;

        public MiniBtn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.TitleBg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            using (SolidBrush b = new SolidBrush(Theme.TitleBg))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
            if (_hover)
            {
                Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 6,
                    Danger ? Theme.Alpha(Theme.Danger, 210)
                    : (Theme.TitleOnBlue ? Theme.Alpha(Theme.TitleText, 46) : Theme.CardHover));
            }
            using (StringFormat sf = Theme.SF(StringAlignment.Center, StringAlignment.Center))
            {
                Color fg = Theme.TitleOnBlue ? Theme.TitleText : (_hover && Danger ? Theme.Danger : Theme.TextDim);
                if (Danger && _hover && Theme.TitleOnBlue) fg = Color.White;
                Theme.Txt(e.Graphics, Glyph, Theme.F(9f), fg, new Rectangle(0, 0, Width, Height), sf);
            }
        }
    }
}

