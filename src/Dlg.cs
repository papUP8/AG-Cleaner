using System;
using System.Drawing;
using System.Windows.Forms;

namespace CCDesk
{
    /// <summary>深色模态对话框：确认、输入、提示。</summary>
    public class DlgForm : Form
    {
        public string Headline = "";
        public string Message = "";
        public TextBox Input;
        public bool Result = false;

        public DlgForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Card;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = Theme.F(9f);
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, 12, Theme.Card);
            Theme.StrokeRound(e.Graphics, r, 12, Theme.Border, 1);
            Theme.Txt(e.Graphics, Headline, Theme.F(12f, FontStyle.Bold), Theme.Text, 24, 20);
            using (StringFormat sf = Theme.SF(StringAlignment.Near, StringAlignment.Near, true))
            {
                Theme.Txt(e.Graphics, Message, Theme.F(9f), Theme.TextDim, new Rectangle(24, 54, Width - 48, Height - 130), sf);
            }
            base.OnPaint(e);
        }
    }

    public static class Dlg
    {
        public static bool Confirm(IWin32Window owner, string headline, string message, string okText, bool danger)
        {
            DlgForm f = new DlgForm();
            f.Headline = headline;
            f.Message = message;
            f.ClientSize = new Size(460, 210);

            Btn cancel = new Btn();
            cancel.Kind = ButtonKind.Subtle;
            cancel.Text = "取消";
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(460 - 24 - 84, 210 - 58);
            cancel.Click += delegate { f.Close(); };
            f.Controls.Add(cancel);

            Btn ok = new Btn();
            ok.Kind = danger ? ButtonKind.Danger : ButtonKind.Accent;
            ok.Text = okText;
            ok.Size = new Size(112, 34);
            ok.Location = new Point(460 - 24 - 84 - 12 - 112, 210 - 58);
            ok.Click += delegate { f.Result = true; f.Close(); };
            f.Controls.Add(ok);

            f.AcceptButton = null;
            f.ShowDialog(owner);
            return f.Result;
        }

        public static string Prompt(IWin32Window owner, string headline, string message, string initial)
        {
            DlgForm f = new DlgForm();
            f.Headline = headline;
            f.Message = message;
            f.ClientSize = new Size(520, 230);

            Panel box = new Panel();
            box.BackColor = Theme.CardSoft;
            box.SetBounds(24, 96, 520 - 48, 34);
            Panel border = new Panel();
            border.BackColor = Theme.Border;
            border.SetBounds(24, 128, 520 - 48, 1);
            f.Controls.Add(border);
            f.Controls.Add(box);

            TextBox input = new TextBox();
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Theme.CardSoft;
            input.ForeColor = Theme.Text;
            input.Font = Theme.Mono(9f);
            input.Text = initial;
            input.SetBounds(10, 8, 520 - 68, 18);
            box.Controls.Add(input);
            f.Input = input;

            Btn cancel = new Btn();
            cancel.Kind = ButtonKind.Subtle;
            cancel.Text = "取消";
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(520 - 24 - 84, 230 - 56);
            cancel.Click += delegate { f.Close(); };
            f.Controls.Add(cancel);

            Btn ok = new Btn();
            ok.Kind = ButtonKind.Accent;
            ok.Text = "确定";
            ok.Size = new Size(96, 34);
            ok.Location = new Point(520 - 24 - 84 - 12 - 96, 230 - 56);
            ok.Click += delegate { f.Result = true; f.Close(); };
            f.Controls.Add(ok);

            f.ShowDialog(owner);
            return f.Result ? input.Text : null;
        }

        public static void Info(IWin32Window owner, string headline, string message)
        {
            DlgForm f = new DlgForm();
            f.Headline = headline;
            f.Message = message;
            f.ClientSize = new Size(460, 200);
            Btn ok = new Btn();
            ok.Kind = ButtonKind.Accent;
            ok.Text = "知道了";
            ok.Size = new Size(96, 34);
            ok.Location = new Point(460 - 24 - 96, 200 - 58);
            ok.Click += delegate { f.Close(); };
            f.Controls.Add(ok);
            f.ShowDialog(owner);
        }
    }
}

