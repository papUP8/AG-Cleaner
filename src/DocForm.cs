using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AGCleaner
{
    /// <summary>内置文档查看器：README、变更记录、版权与许可都编译进 exe，发布时只有一个文件。</summary>
    public class DocForm : Form
    {
        private static readonly string[] Res = new string[] { "README.md", "CHANGES.md", "NOTICE.md", "LICENSE" };
        private static readonly string[] TitleKeys = new string[] { "使用说明", "版本更新记录", "版权与署名", "开源许可证" };

        private TextBox _box;
        private Btn[] _tabs;
        private Btn _close;
        private int _cur = 0;
        private Point _drag;
        private bool _dragging;

        public DocForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Icon = Program.AppIcon();
            BackColor = Theme.Card;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = Theme.F(9f);
            DoubleBuffered = true;
            ClientSize = new Size(760, 560);

            _tabs = new Btn[Res.Length];
            for (int i = 0; i < Res.Length; i++)
            {
                Btn b = new Btn();
                b.Kind = ButtonKind.Subtle;
                b.Size = new Size(120, 30);
                b.Text = Lang.T(TitleKeys[i]);
                int idx = i;
                b.Click += delegate { Show(idx); };
                Controls.Add(b);
                _tabs[i] = b;
            }

            _box = new TextBox();
            _box.Multiline = true;
            _box.ReadOnly = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = Theme.CardSoft;
            _box.ForeColor = Theme.TextDim;
            _box.Font = Theme.F(8.8f);
            _box.WordWrap = true;
            Controls.Add(_box);

            _close = new Btn();
            _close.Kind = ButtonKind.Accent;
            _close.Text = Lang.T("关闭");
            _close.Size = new Size(96, 32);
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            Show(0);
            LayoutAll();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        private void Show(int idx)
        {
            if (idx < 0 || idx >= Res.Length) return;
            _cur = idx;
            string text;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(Res[idx]))
                {
                    if (s == null) text = Lang.T("无法读取内置文档：") + Res[idx];
                    else
                    {
                        using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8))
                        {
                            text = r.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                text = Lang.T("无法读取内置文档：") + Res[idx] + "\r\n" + ex.Message;
            }
            _box.Text = Clean(text);
            _box.SelectionStart = 0;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Kind = i == _cur ? ButtonKind.Accent : ButtonKind.Subtle;
                _tabs[i].Invalidate();
            }
        }

        /// <summary>内置文档以纯文本展示：去掉图片引用（截图不随 exe 分发）与分隔线。</summary>
        private static string Clean(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, "!\\[[^\\]]*\\]\\([^\\)]*\\)", "");
            // 链接保留文字，去掉地址
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\[([^\\]]*)\\]\\([^\\)]*\\)", "$1");
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimEnd();
                string trim = t.TrimStart();
                if (trim == "---" || trim == "***") continue;
                // 只剩表格竖线、分隔符或空格的残留行（原本是截图表格）
                string bare = trim.Replace("|", "").Replace("-", "").Replace(":", "").Replace(" ", "").Replace("\t", "");
                if (bare.Length == 0 && trim.Length > 0) continue;
                // 去掉 Markdown 标记，只留文字
                while (trim.StartsWith("#"))
                {
                    trim = trim.Substring(1).TrimStart();
                    if (trim.Length == 0) break;
                }
                t = trim.Replace("**", "").Replace("`", "");
                sb.Append(t).Append("\r\n");
            }
            return sb.ToString();
        }

        private void LayoutAll()
        {
            int x = 22;
            int y = 62;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].SetBounds(x, y, 132, 30);
                x += 140;
            }
            _box.SetBounds(24, y + 44, ClientSize.Width - 48, ClientSize.Height - y - 44 - 66);
            _close.SetBounds(ClientSize.Width - 24 - 96, ClientSize.Height - 20 - 32, 96, 32);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_tabs != null) LayoutAll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 12, Theme.Card);
            Theme.StrokeRound(g, r, 12, Theme.Border, 1);
            Theme.Txt(g, Lang.T("说明与许可"), Theme.F(12f, FontStyle.Bold), Theme.Text, 24, 22);
            Theme.Txt(g, Lang.T("全部文档已编译进 AG-Cleaner.exe，不必附带任何外部文件"),
                Theme.F(8f), Theme.Muted, 24, 44);
            base.OnPaint(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < 52)
            {
                _dragging = true;
                _drag = e.Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = PointToScreen(e.Location);
                Location = new Point(p.X - _drag.X, p.Y - _drag.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
