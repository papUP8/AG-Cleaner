using System;
using System.Drawing;
using System.Windows.Forms;

namespace AGCleaner
{
    /// <summary>语言选择：列出全部语言，选中即返回语言代码。</summary>
    public class LangPicker : Form
    {
        public string Chosen = null;

        public LangPicker()
        {
            FormBorderStyle = FormBorderStyle.None;
            Icon = Program.AppIcon();
            BackColor = Theme.Card;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = Theme.F(9f);
            DoubleBuffered = true;
            ClientSize = new Size(260, 60 + Lang.Codes.Length * 42);

            for (int i = 0; i < Lang.Codes.Length; i++)
            {
                string code = Lang.Codes[i];
                Btn b = new Btn();
                b.Kind = code == Lang.Cur ? ButtonKind.Accent : ButtonKind.Ghost;
                b.Text = Lang.NativeName(code) + (code == "zh" ? "" : "  ·  " + code.ToUpperInvariant());
                b.Size = new Size(212, 34);
                b.Location = new Point(24, 50 + i * 42);
                b.Click += delegate { Chosen = code; Close(); };
                Controls.Add(b);
            }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, 12, Theme.Card);
            Theme.StrokeRound(e.Graphics, r, 12, Theme.Border, 1);
            Theme.Txt(e.Graphics, Lang.T("界面语言 / Language"), Theme.F(11f, FontStyle.Bold), Theme.Text, 24, 18);
            base.OnPaint(e);
        }
    }

    public static class LangSwitch
    {
        /// <summary>弹出语言菜单；选中的语言立即生效并写入配置，返回 true 表示语言变了。</summary>
        public static bool Pick(IWin32Window owner)
        {
            using (LangPicker p = new LangPicker())
            {
                p.ShowDialog(owner);
                if (p.Chosen == null || p.Chosen == Lang.Cur) return false;
                Lang.Set(p.Chosen);
                Core.Settings st = Core.Settings.Load();
                st.Language = p.Chosen;
                st.Save();
                return true;
            }
        }
    }
}
