using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    public class StartupPage : PageBase
    {
        public override string PageTitle { get { return "启动项管理"; } }
        public override string PageSubtitle { get { return "注册表 Run 与启动文件夹项目，禁用时自动备份，可随时还原"; } }

        private VList _list;
        private ActionBar _bar;
        private Btn _refresh;
        private Btn _showDisabled;
        private List<StartupItem> _items = new List<StartupItem>();
        private List<StartupItem> _disabled = new List<StartupItem>();
        private bool _busy;
        private bool _showOff = true;
        private string _status = "读取启动项...";
        private Spinner _spinner;

        public StartupPage()
        {
            _refresh = new Btn();
            _refresh.Kind = ButtonKind.Subtle;
            _refresh.Text = "刷新";
            _refresh.Size = new Size(80, 32);
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _showDisabled = new Btn();
            _showDisabled.Kind = ButtonKind.Link;
            _showDisabled.Text = "已禁用项";
            _showDisabled.Size = new Size(88, 30);
            _showDisabled.Click += delegate
            {
                _showOff = !_showOff;
                _showDisabled.Kind = _showOff ? ButtonKind.Link : ButtonKind.Subtle;
                _showDisabled.Invalidate();
                Fill();
            };
            Controls.Add(_showDisabled);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = "状态";
            _list.RowHeight = 58;
            _list.EmptyText = "没有启动项";
            _list.EmptyHint = "注册表与启动文件夹均为空";
            _list.ActionClicked += delegate { ToggleSelected(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _refresh.SetBounds(0, y, 80, 32);
            _showDisabled.SetBounds(88, y + 2, 88, 30);
            _bar.SetBounds(0, Height - 58, w, 58);
            _list.SetBounds(0, y + 44, w, Height - (y + 44) - 92);
            _spinner.SetBounds(w - 40, y + 4, 24, 24);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            Load();
        }

        public void Load()
        {
            if (_busy) return;
            _busy = true;
            _spinner.Spin(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> on = null;
                List<StartupItem> off = null;
                try
                {
                    on = Startup.List();
                    off = Startup.ListDisabled();
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _items = on == null ? new List<StartupItem>() : on;
                        _disabled = off == null ? new List<StartupItem>() : off;
                        Fill();
                    });
                }
                catch (Exception) { }
            });
        }

        private void Fill()
        {
            List<VRow> rows = new List<VRow>();
            for (int i = 0; i < _items.Count; i++)
            {
                StartupItem it = _items[i];
                VRow r = new VRow();
                r.Title = it.Name;
                r.Sub = Theme.ShortPath(it.Command, 84);
                r.Tag = it.Source;
                r.TagColor = it.IsFile ? Theme.Blue : Theme.Accent;
                r.Right = "已启用";
                r.Action = "禁用";
                r.ActionColor = Theme.Warn;
                r.Data = it;
                rows.Add(r);
            }
            if (_showOff)
            {
                for (int i = 0; i < _disabled.Count; i++)
                {
                    StartupItem it = _disabled[i];
                    VRow r = new VRow();
                    r.Title = it.Name;
                    r.Sub = Theme.ShortPath(it.Command, 84);
                    r.Tag = it.Source;
                    r.TagColor = Theme.Muted;
                    r.Right = "已禁用";
                    r.Alarm = false;
                    r.Action = "启用";
                    r.ActionColor = Theme.Accent;
                    r.Data = it;
                    rows.Add(r);
                }
            }
            _list.SetRows(rows);
            _status = "启用中 " + _items.Count + " 项 · 已禁用 " + _disabled.Count + " 项（禁用项已备份，可还原）";
            _bar.Invalidate();
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.Txt(e.Graphics, "启动项直接拖慢开机速度，禁用不影响程序手动启动", Theme.F(8.5f), Theme.TextDim, 4, 12);
            Theme.Txt(e.Graphics, _status, Theme.F(8f), Theme.Muted, 4, 34);
        }

        private void ToggleSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            StartupItem it = r.Data as StartupItem;
            if (it == null) return;
            string message = "";
            bool ok;
            if (it.Enabled)
            {
                bool confirm = Dlg.Confirm(FindForm(), "禁用启动项 " + it.Name + "？",
                    "命令：" + Theme.ShortPath(it.Command, 90) + "\n\n禁用会从启动位置移除该项，原始数据已备份，可在此页随时“启用”还原。",
                    "禁用", false);
                if (!confirm) return;
                ok = Startup.Disable(it, out message);
            }
            else
            {
                ok = Startup.Enable(it, out message);
            }
            _status = message;
            _bar.Invalidate();
            Load();
        }
    }
}

