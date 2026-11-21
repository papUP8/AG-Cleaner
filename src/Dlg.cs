using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AGCleaner
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
            Icon = Program.AppIcon();
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

    /// <summary>结果报告框：可滚动的完整明细列表，不再截断。</summary>
    public class ReportForm : Form
    {
        public string Headline = "";
        public string Summary = "";
        public string Hint = "";

        public ReportForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Icon = Program.AppIcon();
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
            Theme.Txt(e.Graphics, Headline, Theme.F(12f, FontStyle.Bold), Theme.Text, 24, 18);
            Theme.Txt(e.Graphics, Summary, Theme.F(9f), Theme.Accent, 24, 44);
            if (Hint.Length > 0)
                Theme.Txt(e.Graphics, Hint, Theme.F(7.8f), Theme.Muted, 24, Height - 120);
            base.OnPaint(e);
        }
    }

    public static class Dlg
    {
        /// <summary>完整结果报告：明细不截断，可滚动查看，并可打开操作日志。</summary>
        public static void Report(IWin32Window owner, string headline, string summary, List<string> lines, string hint)
        {
            ReportForm f = new ReportForm();
            f.Headline = headline;
            f.Summary = summary;
            f.Hint = hint == null ? "" : hint;
            f.ClientSize = new Size(660, 500);

            VList list = new VList();
            list.ShowCheck = false;
            list.RightHeader = "";
            list.RowHeight = 28;
            list.EmptyText = Lang.T("没有明细记录");
            list.EmptyHint = Lang.T("选中的项目可能都不需要实际删除");
            list.SetBounds(20, 76, 660 - 40, 500 - 76 - 124);
            List<VRow> rows = new List<VRow>();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    VRow r = new VRow();
                    r.Title = lines[i];
                    r.Tag = "";
                    rows.Add(r);
                }
            }
            list.SetRows(rows);
            f.Controls.Add(list);

            Btn logBtn = new Btn();
            logBtn.Kind = ButtonKind.Subtle;
            logBtn.Text = Lang.T("打开操作日志");
            logBtn.Size = new Size(112, 34);
            logBtn.Location = new Point(20, 500 - 52);
            logBtn.Click += delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", AGCleaner.Core.Store.OpLogFile); }
                catch (Exception) { }
            };
            f.Controls.Add(logBtn);

            Btn ok = new Btn();
            ok.Kind = ButtonKind.Accent;
            ok.Text = Lang.T("完成");
            ok.Size = new Size(96, 34);
            ok.Location = new Point(660 - 24 - 96, 500 - 52);
            // 在 MouseDown 就关闭：避免对话框销毁后同一次点击的 MouseUp 落到主窗口按钮上，
            // 造成“清理完成”对话框一关就再次触发清理的死循环
            ok.MouseDown += delegate { f.Close(); };
            f.Controls.Add(ok);
            ok.Select();
            f.ShowDialog(owner);
        }

        public static bool Confirm(IWin32Window owner, string headline, string message, string okText, bool danger)
        {
            DlgForm f = new DlgForm();
            f.Headline = headline;
            f.Message = message;
            f.ClientSize = new Size(460, 210);

            Btn cancel = new Btn();
            cancel.Kind = ButtonKind.Subtle;
            cancel.Text = Lang.T("取消");
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(460 - 24 - 84, 210 - 58);
            cancel.Click += delegate { f.Close(); };
            f.Controls.Add(cancel);

            Btn ok = new Btn();
            ok.Kind = danger ? ButtonKind.Danger : ButtonKind.Accent;
            ok.Text = okText;
            ok.Size = new Size(112, 34);
            ok.Location = new Point(460 - 24 - 84 - 12 - 112, 210 - 58);
            ok.MouseDown += delegate { f.Result = true; f.Close(); };
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
            cancel.Text = Lang.T("取消");
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(520 - 24 - 84, 230 - 56);
            cancel.Click += delegate { f.Close(); };
            f.Controls.Add(cancel);

            Btn ok = new Btn();
            ok.Kind = ButtonKind.Accent;
            ok.Text = Lang.T("确定");
            ok.Size = new Size(96, 34);
            ok.Location = new Point(520 - 24 - 84 - 12 - 96, 230 - 56);
            ok.MouseDown += delegate { f.Result = true; f.Close(); };
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
            ok.Text = Lang.T("知道了");
            ok.Size = new Size(96, 34);
            ok.Location = new Point(460 - 24 - 96, 200 - 58);
            ok.Click += delegate { f.Close(); };
            f.Controls.Add(ok);
            f.ShowDialog(owner);
        }
    }
}

