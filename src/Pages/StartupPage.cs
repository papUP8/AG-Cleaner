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
        public override string PageSubtitle { get { return "用户启动项、计划任务、自动插件、应用软件服务、系统关键服务；禁用前自动备份，可一键还原"; } }

        private VList _list;
        private ActionBar _bar;
        private Btn _refresh;
        private List<StartupItem> _items = new List<StartupItem>();
        private List<Chip> _chips = new List<Chip>();
        private bool _busy;
        private string _filter = "all";
        private string _status = "读取启动项...";
        private Spinner _spinner;
        private ProgressBar2 _prog;

        public StartupPage()
        {
            _refresh = new Btn();
            _refresh.Kind = ButtonKind.Subtle;
            _refresh.Text = "刷新";
            _refresh.Size = new Size(80, 32);
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = "状态";
            _list.RowHeight = 58;
            _list.EmptyText = "没有启动项";
            _list.EmptyHint = "当前分类下没有项目";
            _list.ActionClicked += delegate { ToggleSelected(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);

            _prog = new ProgressBar2();
            _prog.Visible = false;
            Controls.Add(_prog);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _refresh.SetBounds(0, y, 80, 32);
            int cx = 92;
            for (int i = 0; i < _chips.Count; i++)
            {
                Graphics g = CreateGraphics();
                int cw = _chips[i].FitWidth(g);
                g.Dispose();
                _chips[i].SetBounds(cx, y + 2, cw, 28);
                cx += cw + 8;
            }
            _bar.SetBounds(0, Height - 58, w, 58);
            int top = y + 44;
            if (_prog != null && _prog.Visible)
            {
                _prog.SetBounds(0, top, w, 44);
                top += 52;
            }
            _list.SetBounds(0, top, w, Height - top - 92);
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
            _prog.Visible = true;
            _prog.BringToFront();
            _prog.SetProgress(5, "读取注册表与服务列表...");
            LayoutChildren();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> on = null;
                try { on = Startup.List(); }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _prog.Visible = false;
                        LayoutChildren();
                        _items = on == null ? new List<StartupItem>() : on;
                        BuildChips();
                        Fill();
                    });
                }
                catch (Exception) { }
            });
        }

        private void BuildChips()
        {
            for (int i = 0; i < _chips.Count; i++) Controls.Remove(_chips[i]);
            _chips.Clear();
            _filter = "all";

            int user = 0, sys = 0, task = 0, plugin = 0, appsvc = 0, syssvc = 0, off = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                StartupItem it = _items[i];
                if (!it.Enabled) off++;
                if (it.Group == "user") user++;
                else if (it.Group == "sys") sys++;
                else if (it.Group == "task") task++;
                else if (it.Group == "plugin") plugin++;
                else if (it.Group == "appsvc") appsvc++;
                else if (it.Group == "syssvc") syssvc++;
            }

            Chip all = MakeChip("全部", _items.Count, "all", true);
            MakeChip("用户启动项", user, "user", false);
            MakeChip("计划任务", task, "task", false);
            MakeChip("自动插件", plugin, "plugin", false);
            MakeChip("应用软件服务", appsvc, "appsvc", false);
            MakeChip("系统关键服务", syssvc, "syssvc", false);
            MakeChip("已禁用", off, "disabled", false);
            LayoutChildren();
        }

        private Chip MakeChip(string name, int count, string id, bool active)
        {
            Chip chip = new Chip();
            chip.Text = name;
            chip.Count = count;
            chip.Active = active;
            chip.Click += delegate
            {
                _filter = id;
                for (int i = 0; i < _chips.Count; i++)
                {
                    _chips[i].Active = _chips[i] == chip;
                    _chips[i].Invalidate();
                }
                Fill();
            };
            Controls.Add(chip);
            _chips.Add(chip);
            chip.BringToFront();
            return chip;
        }

        private void Fill()
        {
            List<VRow> rows = new List<VRow>();
            int shown = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                StartupItem it = _items[i];
                if (!Match(it)) continue;
                shown++;
                VRow r = new VRow();
                r.Title = it.Name;
                r.Sub = Theme.ShortPath(it.Command, 86);
                r.Tag = it.GroupText;
                r.TagColor = GroupColor(it.Group);
                r.Right = it.Enabled ? "已启用" : "已禁用";
                r.Alarm = !it.Enabled;
                r.Data = it;
                if (it.IsService)
                {
                    r.Action = "";
                    r.Disabled = false;
                }
                else
                {
                    r.Action = it.Enabled ? "禁用" : "启用";
                    r.ActionColor = it.Enabled ? Theme.Warn : Theme.Accent;
                }
                rows.Add(r);
            }
            _list.SetRows(rows);
            int off = 0;
            for (int i = 0; i < _items.Count; i++) if (!_items[i].Enabled) off++;
            _status = "当前显示 " + shown + " 项 · 共 " + _items.Count + " 项，其中已禁用 " + off + " 项；服务项仅展示，不做修改";
            _bar.Invalidate();
        }

        private bool Match(StartupItem it)
        {
            if (_filter == "all") return true;
            if (_filter == "disabled") return !it.Enabled;
            return it.Group == _filter;
        }

        private Color GroupColor(string g)
        {
            if (g == "sys") return Theme.Danger;
            if (g == "task") return Theme.Warn;
            if (g == "plugin") return Theme.C(Theme.IsLight ? "#0E7490" : "#38BDF8");
            if (g == "appsvc") return Theme.C(Theme.IsLight ? "#8B5CF6" : "#8B7CF6");
            if (g == "syssvc") return Theme.C(Theme.IsLight ? "#B45309" : "#F59E0B");
            if (g == "service") return Theme.C(Theme.IsLight ? "#8B5CF6" : "#8B7CF6");
            return Theme.Accent;
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.Txt(e.Graphics, "禁用只影响开机自启，不会删除程序；注册表项与文件均先备份", Theme.F(8.5f), Theme.TextDim, 4, 12);
            Theme.Txt(e.Graphics, _status, Theme.F(8f), Theme.Muted, 4, 34);
        }

        private void ToggleSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            StartupItem it = r.Data as StartupItem;
            if (it == null) return;
            if (it.IsService)
            {
                Dlg.Info(FindForm(), "系统服务仅展示", "服务项涉及系统组件，CCDesk 不在此处启停。\n如需调整，请在“服务”管理器中操作，或使用系统配置工具。");
                return;
            }
            string message = "";
            bool ok;
            if (it.Enabled)
            {
                string mode = it.IsTask ? "计划任务" : (it.IsFile ? "启动文件夹项" : "注册表启动项");
                bool confirm = Dlg.Confirm(FindForm(), "禁用" + mode + " " + it.Name + "？",
                    "命令：" + Theme.ShortPath(it.Command, 90) + "\n\n禁用只阻止它开机自动运行，不会删除程序本身。原始数据会自动备份，可在此页随时“启用”还原。",
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
