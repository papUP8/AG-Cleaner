using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    public class OptimizePage : PageBase
    {
        public override string PageTitle { get { return Lang.T("系统优化"); } }
        public override string PageSubtitle { get { return Lang.T("网络缓存刷新、磁盘 TRIM 与更新缓存清理，安全可控"); } }

        private VList _list;
        private Card _logCard;
        private TextBox _log;
        private Btn _adminBtn;
        private Btn _logClear;
        private List<OptimizeAction> _actions = new List<OptimizeAction>();
        private bool _busy;

        public OptimizePage()
        {
            _actions = Optimizer.Actions();

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = "";
            _list.RowHeight = 74;
            _list.EmptyText = Lang.T("没有可用的优化项");
            _list.ActionClicked += delegate { RunSelected(); };
            Controls.Add(_list);

            _logCard = new Card();
            _logCard.Paint += LogPaint;
            Controls.Add(_logCard);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.None;
            _log.WordWrap = true;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.CardSoft;
            _log.ForeColor = Theme.TextDim;
            _log.Font = Theme.Mono(8.5f);
            _logCard.Controls.Add(_log);

            _logClear = new Btn();
            _logClear.Kind = ButtonKind.Link;
            _logClear.Text = Lang.T("清空");
            _logClear.Size = new Size(48, 24);
            _logClear.Click += delegate { _log.Text = ""; };
            _logCard.Controls.Add(_logClear);

            _adminBtn = new Btn();
            _adminBtn.Kind = ButtonKind.Ghost;
            _adminBtn.Text = Lang.T("以管理员身份重启");
            _adminBtn.Size = new Size(150, 32);
            _adminBtn.Click += delegate
            {
                if (Dlg.Confirm(FindForm(), Lang.T("以管理员身份重启？"), Lang.T("将以 UAC 提权方式重新启动 AG Cleaner，解锁 ARP 缓存、磁盘 TRIM 与 Windows 更新缓存清理。当前窗口会关闭。"), Lang.T("重启"), false))
                {
                    if (Native.RelaunchAsAdmin()) Application.Exit();
                }
            };
            Controls.Add(_adminBtn);

            Fill();
            LayoutChildren();
            Append(Lang.T("AG Cleaner 系统优化 · ") + (Native.IsAdmin() ? Lang.T("当前为管理员权限") : Lang.T("当前为普通用户权限，标记“需要管理员”的项目会被拦截")));
        }

        private void Fill()
        {
            List<VRow> rows = new List<VRow>();
            for (int i = 0; i < _actions.Count; i++)
            {
                OptimizeAction a = _actions[i];
                VRow r = new VRow();
                r.Title = a.Name;
                r.Sub = a.Desc;
                r.Right = "";
                if (a.AdminOnly) { r.Tag = Lang.T("需要管理员"); r.TagColor = Theme.Warn; }
                r.Action = Lang.T("执行");
                r.ActionColor = Theme.Accent;
                r.Data = a;
                rows.Add(r);
            }
            _list.SetRows(rows);
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _adminBtn.Visible = !Native.IsAdmin();
            if (_adminBtn.Visible) _adminBtn.SetBounds(w - 150, y, 150, 32);
            int listH = _actions.Count * _list.RowHeight + 34;
            int avail = Height - y - 150;
            if (listH > avail) listH = avail;
            if (listH < 120) listH = 120;
            _list.SetBounds(0, y + (Native.IsAdmin() ? 0 : 42), w, listH);
            int logY = _list.Bottom + 14;
            _logCard.SetBounds(0, logY, w, Height - logY - 4);
            _log.SetBounds(18, 46, w - 40, Math.Max(40, _logCard.Height - 60));
            _logClear.SetBounds(w - 66, 18, 48, 24);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LogPaint(object sender, PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.Txt(e.Graphics, Lang.T("执行日志"), Theme.F(9.5f, FontStyle.Bold), Theme.Text, 18, 18);
        }

        private void Append(string line)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _log.AppendText("[" + ts + "] " + line + "\r\n");
        }

        private void RunSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            OptimizeAction a = r.Data as OptimizeAction;
            if (a == null) return;
            if (_busy)
            {
                Append(Lang.T("已有任务在执行，请稍候"));
                return;
            }
            if (a.AdminOnly && !Native.IsAdmin())
            {
                Dlg.Info(FindForm(), Lang.T("需要管理员权限"), Lang.T("该操作需要管理员权限，请点击右上角“以管理员身份重启”后再执行。"));
                return;
            }
            if (a.Id == "recycle")
            {
                long size = 0;
                int items = 0;
                Shell.RecycleBinInfo(ref size, ref items);
                if (!Dlg.Confirm(FindForm(), Lang.T("清空回收站？"), Lang.T("将删除回收站中的 ") + items + Lang.T(" 项文件（") + Byt.Format(size) + Lang.T("），此操作不可恢复。"), Lang.T("清空"), true)) return;
            }
            if (a.LongRunning)
            {
                if (!Dlg.Confirm(FindForm(), Lang.T("执行 ") + a.Name + "？", a.Desc + Lang.T("\n\n可能需要数分钟，期间请勿关闭窗口。"), Lang.T("开始执行"), false)) return;
            }

            _busy = true;
            Append(Lang.T("▶ 开始：") + a.Name);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string summary = "";
                bool ok = false;
                try
                {
                    ok = Optimizer.Run(a, delegate (string line)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate { Append("  " + line); });
                        }
                        catch (Exception) { }
                    }, out summary);
                }
                catch (Exception ex) { summary = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        Append((ok ? "✔ " : "✖ ") + a.Name + "：" + summary);
                    });
                }
                catch (Exception) { }
            });
        }
    }
}

