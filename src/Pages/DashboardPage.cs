using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    public class StatCard : Control
    {
        public string Title = "";
        public string Value = "—";
        public string Sub = "";
        public string Glyph = "";
        public double Fraction = -1;
        public Color BarColor = Theme.Accent;
        private bool _hover;

        public StatCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 12, _hover ? Theme.CardHover : Theme.Card);
            Theme.StrokeRound(g, r, 12, Theme.Border, 1);

            Theme.Txt(g, Title, Theme.F(8.3f), Theme.Muted, 18, 14);
            if (Glyph.Length > 0)
            {
                using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
                {
                    Theme.Txt(g, Glyph, Theme.F(11f), Theme.Alpha(BarColor, 120), new Rectangle(Width - 46, 10, 32, 24), sf);
                }
            }
            Theme.Txt(g, Value, Theme.F(17f, FontStyle.Bold), Theme.Text, 17, 36);
            Theme.Txt(g, Sub, Theme.F(8f), Theme.Muted, 18, 72);

            if (Fraction >= 0)
            {
                int barW = Width - 36;
                Theme.FillRound(g, new Rectangle(18, Height - 20, barW, 4), 2, Theme.Track);
                int fw = (int)(barW * Math.Max(0, Math.Min(1, Fraction)));
                if (fw > 1) Theme.FillRound(g, new Rectangle(18, Height - 20, fw, 4), 2, BarColor);
            }
        }
    }

    public class DashboardPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("概览"); } }
        public override string PageSubtitle { get { return Lang.T("系统健康、占用与可释放空间一览，可一键开始智能清理"); } }

        private StatCard[] _cards = new StatCard[4];
        private Card _healthCard;
        private Ring _ring;
        private Card _actionCard;
        private Card _procCard;
        private Btn _scanBtn;
        private Sampler _sampler = new Sampler();
        private Sample _sample;
        private int _startupCount = 0;
        private List<string> _notes = new List<string>();
        private int _score = 100;
        private bool _scanning;
        private long _cleanable = -1;
        private string _scanMsg = Lang.T("正在统计可清理空间...");
        private DateTime _lastScan = DateTime.MinValue;

        public DashboardPage()
        {
            for (int i = 0; i < 4; i++)
            {
                StatCard c = new StatCard();
                _cards[i] = c;
                Controls.Add(c);
            }
            _cards[0].Title = Lang.T("系统盘剩余");
            _cards[0].Glyph = "▤";
            _cards[0].BarColor = Theme.Blue;
            _cards[1].Title = Lang.T("可清理空间");
            _cards[1].Glyph = "✦";
            _cards[1].BarColor = Theme.Accent;
            _cards[2].Title = Lang.T("内存占用");
            _cards[2].Glyph = "▮";
            _cards[2].BarColor = Theme.C("#8B7CF6");
            _cards[3].Title = Lang.T("CPU 使用");
            _cards[3].Glyph = "◉";
            _cards[3].BarColor = Theme.Warn;

            _healthCard = new Card();
            _healthCard.Paint += HealthPaint;
            Controls.Add(_healthCard);

            _ring = new Ring();
            _ring.BackColor = Theme.Card;
            _healthCard.Controls.Add(_ring);

            _actionCard = new Card();
            _actionCard.Paint += ActionPaint;
            Controls.Add(_actionCard);

            _scanBtn = new Btn();
            _scanBtn.Kind = ButtonKind.Accent;
            _scanBtn.Text = Lang.T("开始智能清理");
            _scanBtn.Glyph = "";
            _scanBtn.Height = 40;
            _scanBtn.Click += delegate
            {
                MainForm mf = FindForm() as MainForm;
                if (mf != null)
                {
                    mf.SelectPage(1);
                    CleanPage cp = mf.PageAt(1) as CleanPage;
                    if (cp != null) cp.StartScan();
                }
            };
            _actionCard.Controls.Add(_scanBtn);

            _procCard = new Card();
            _procCard.Paint += ProcPaint;
            Controls.Add(_procCard);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            if (_cards[0] == null) return;
            int w = Width;
            int gap = 14;
            int cw = (w - gap * 3) / 4;
            for (int i = 0; i < 4; i++)
            {
                _cards[i].SetBounds(i * (cw + gap), HeaderH + 6, cw, 104);
            }
            int leftW = (int)(w * 0.63);
            _healthCard.SetBounds(0, HeaderH + 124, leftW, 306);
            _ring.SetBounds(26, 78, 168, 168);
            _actionCard.SetBounds(leftW + gap, HeaderH + 124, w - leftW - gap, 306);
            _scanBtn.SetBounds(22, 306 - 68, w - leftW - gap - 44, 40);
            _procCard.SetBounds(0, HeaderH + 444, w, Math.Max(120, Height - HeaderH - 452));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            if (_sample == null) _sample = _sampler.Next();
            UpdateFromSample();
            if ((DateTime.Now - _lastScan).TotalSeconds > 45) StartQuickScan();
            if (_startupCount == 0)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    int n = 0;
                    try { n = Startup.List().Count; } catch (Exception) { }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _startupCount = n;
                            _procCard.Invalidate();
                        });
                    }
                    catch (Exception) { }
                });
            }
        }

        private void StartQuickScan()
        {
            if (_scanning) return;
            _scanning = true;
            _scanMsg = Lang.T("正在统计可清理空间...");
            _cards[1].Value = "...";
            _cards[1].Sub = Lang.T("扫描缓存与临时文件");
            _cards[1].Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                long total = 0;
                try
                {
                    Settings st = Settings.Load();
                    List<CleanCategory> cats = CleanEngine.Scan(st, (Action<string>)null);
                    for (int i = 0; i < cats.Count; i++)
                    {
                        if (cats[i].Id == "gpu" || cats[i].Id == "projects" || cats[i].Id == "recycle") continue;
                        total += cats[i].TotalBytes();
                    }
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _scanning = false;
                        _cleanable = total;
                        _lastScan = DateTime.Now;
                        _scanMsg = Lang.T("已扫描：") + Byt.Format(total);
                        _cards[1].Value = Byt.Format(total);
                        _cards[1].Sub = Lang.T("临时 / 缓存 / 日志文件");
                        _cards[1].Fraction = total > 0 ? Math.Min(1.0, (double)total / (12.0 * 1024 * 1024 * 1024)) : 0;
                        _cards[1].Invalidate();
                    });
                }
                catch (Exception) { }
            });
        }

        private void UpdateFromSample()
        {
            if (_sample == null) return;
            string sysDrive = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            long free = Disk.FreeSpace(sysDrive);
            long total = Disk.TotalSpace(sysDrive);
            _cards[0].Value = Byt.Format(free);
            _cards[0].Sub = Lang.T("共 ") + Byt.Format(total) + " · " + sysDrive.TrimEnd('\\');
            _cards[0].Fraction = total > 0 ? (double)(total - free) / total : 0;
            _cards[0].Invalidate();

            _cards[2].Value = ((int)_sample.MemPercent).ToString() + "%";
            _cards[2].Sub = Lang.T("已用 ") + Byt.Format(_sample.MemUsed) + " / " + Byt.Format(_sample.MemTotal);
            _cards[2].Fraction = _sample.MemPercent / 100.0;
            _cards[2].Invalidate();

            _cards[3].Value = ((int)_sample.CpuPercent).ToString() + "%";
            _cards[3].Sub = Lang.T("进程 ") + _sample.ProcCount + Lang.T(" 个 · 网络 ↓ ") + Byt.Format(_sample.NetDown) + "/s";
            _cards[3].Fraction = _sample.CpuPercent / 100.0;
            _cards[3].Invalidate();
        }

        public override void Tick()
        {
            _sample = _sampler.Next();
            UpdateFromSample();
            _score = Sampler.Health(_sample, _startupCount, out _notes);
            _ring.SetScore(_score);
            _ring.Tick();
            _ring.Invalidate();
            _healthCard.Invalidate();
            _procCard.Invalidate();
        }

        private void HealthPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("系统健康"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 26, 22);
            Theme.Txt(g, Lang.T("评分由磁盘、内存、CPU 与启动项综合计算"), Theme.F(8f), Theme.Muted, 26, 46);

            int x = 214;
            int w = c.Width - x - 26;
            if (w < 120) return;
            DrawMetric(g, x, 92, w, "CPU", _sample == null ? 0 : _sample.CpuPercent / 100.0,
                _sample == null ? "—" : ((int)_sample.CpuPercent).ToString() + "%", Theme.Warn);
            DrawMetric(g, x, 132, w, Lang.T("内存"), _sample == null ? 0 : _sample.MemPercent / 100.0,
                _sample == null ? "—" : ((int)_sample.MemPercent).ToString() + "%", Theme.C("#8B7CF6"));
            DrawMetric(g, x, 172, w, Lang.T("磁盘"), _sample == null ? 0 : _sample.DiskPercent / 100.0,
                _sample == null ? "—" : ((int)_sample.DiskPercent).ToString() + "%", Theme.Blue);

            Theme.Txt(g, Lang.T("诊断建议"), Theme.F(8.3f), Theme.Muted, 214, 204);
            int y = 224;
            if (_notes != null)
            {
                for (int i = 0; i < _notes.Count && i < 3; i++)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                    {
                        g.FillEllipse(b, 216, y + 4, 4, 4);
                    }
                    using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
                    {
                        Theme.Txt(g, _notes[i], Theme.F(8.3f), Theme.TextDim, new Rectangle(228, y, c.Width - 244, 20), sf);
                    }
                    y += 20;
                }
            }
        }

        private void DrawMetric(Graphics g, int x, int y, int w, string label, double value, string text, Color color)
        {
            Theme.Txt(g, label, Theme.F(8.3f), Theme.TextDim, x, y);
            Theme.Txt(g, text, Theme.F(8.6f), Theme.Text, x + 44, y - 1);
            int bx = x + 96;
            int bw = w - 96;
            if (bw < 40) return;
            Theme.FillRound(g, new Rectangle(bx, y + 5, bw, 5), 2, Theme.Track);
            int fw = (int)(bw * Math.Max(0, Math.Min(1, value)));
            if (fw > 2) Theme.FillRound(g, new Rectangle(bx, y + 5, fw, 5), 2, color);
        }

        private void ActionPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("智能清理"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 22, 22);
            using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near, true))
            {
                Theme.Txt(g, Lang.T("按安全目录扫描临时文件、浏览器与应用缓存，先审阅清单再删除。"),
                    Theme.F(8.3f), Theme.Muted, new Rectangle(22, 50, c.Width - 44, 60), sf);
            }

            int y = 112;
            string[] lines = new string[]
            {
                Lang.T("· 受保护目录与白名单自动跳过"),
                Lang.T("· 默认把删除项移入回收站"),
                Lang.T("· 每一步写入操作日志可追溯")
            };
            for (int i = 0; i < lines.Length; i++)
            {
                Theme.Txt(g, lines[i], Theme.F(8.3f), Theme.TextDim, 22, y + i * 21);
            }

            Theme.Txt(g, _scanMsg, Theme.F(8f), Theme.Accent, 22, c.Height - 108);
            if (_cleanable >= 0)
            {
                Theme.Txt(g, Lang.T("预计可释放 ") + Byt.Format(_cleanable), Theme.F(9.5f, FontStyle.Bold), Theme.Text, 22, c.Height - 86);
            }
        }

        private void ProcPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("占用最高的进程"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 22, 18);
            using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
            {
                Theme.Txt(g, Lang.T("CPU / 内存"), Theme.F(8f), Theme.Muted, new Rectangle(c.Width - 160, 20, 138, 20), sf);
            }

            if (_sample == null || _sample.Top.Count == 0) return;
            int y = 50;
            for (int i = 0; i < _sample.Top.Count; i++)
            {
                ProcInfo p = _sample.Top[i];
                using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near))
                {
                    Theme.Txt(g, p.Name, Theme.F(8.8f), Theme.TextDim, new Rectangle(22, y, c.Width - 220, 20), sf);
                }
                using (StringFormat sf = Theme.SF(StringAlignment.Far, StringAlignment.Near))
                {
                    string right = p.Cpu.ToString("0.0") + "%   " + Byt.Format(p.Memory);
                    Theme.Txt(g, right, Theme.F(8.4f), Theme.Muted, new Rectangle(c.Width - 230, y, 208, 20), sf);
                }
                y += 24;
                if (y > c.Height - 24) break;
            }
        }
    }
}

