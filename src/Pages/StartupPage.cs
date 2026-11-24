using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    public class StartupPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("启动项管理"); } }
        public override string PageSubtitle { get { return Lang.T("用户启动项、计划任务、自动插件、应用软件服务、系统关键服务；禁用前自动备份，可一键还原"); } }

        private VList _list;
        private ActionBar _bar;
        private Btn _refresh;
        private List<StartupItem> _items = new List<StartupItem>();
        private List<Chip> _chips = new List<Chip>();
        private bool _busy;
        private string _filter = "all";
        private string _status = Lang.T("读取启动项...");
        private Spinner _spinner;
        private ProgressBar2 _prog;
        private CheckBox2 _hide;
        private bool _hideOff = true;
        /// <summary>截图自检用：指定初始筛选分类（all/user/task/plugin/appsvc/syssvc/disabled）。</summary>
        public static string PendingFilter = "";

        public StartupPage()
        {
            _refresh = new Btn();
            _refresh.Kind = ButtonKind.Subtle;
            _refresh.Text = Lang.T("刷新");
            _refresh.Size = new Size(80, 32);
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _hide = new CheckBox2();
            _hide.Text = Lang.T("隐藏已禁用项");
            _hide.Checked = true;
            _hide.Size = new Size(148, 30);
            _hide.Changed += delegate
            {
                _hideOff = _hide.Checked;
                Fill();
            };
            Controls.Add(_hide);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = Lang.T("状态");
            _list.RowHeight = 86;
            _list.EmptyText = Lang.T("没有启动项");
            _list.EmptyHint = Lang.T("当前分类下没有项目");
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
            _hide.SetBounds(w - 168, y + 1, 148, 30);
            int cx = 92;
            int cy = y + 2;
            int rows = 1;
            for (int i = 0; i < _chips.Count; i++)
            {
                Graphics g = CreateGraphics();
                int cw = _chips[i].FitWidth(g);
                g.Dispose();
                // 右侧要给“隐藏已禁用项”勾选框留位置，放不下就换行，避免压字
                if (cx + cw > w - 176 && cx > 92)
                {
                    cx = 92;
                    cy += 34;
                    rows++;
                }
                _chips[i].SetBounds(cx, cy, cw, 28);
                cx += cw + 8;
            }
            _bar.SetBounds(0, Height - 58, w, 58);
            int top = y + 44 + (rows - 1) * 34;
            if (_prog != null && _prog.Visible)
            {
                _prog.SetBounds(0, top, w, 44);
                top += 52;
            }
            _list.SetBounds(0, top, w, Height - top - 72);
            _spinner.SetBounds(w - 56, y, 44, 44);
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
            _spinner.Spin(false); // 加载提示统一放在列表正中
            _prog.Visible = false;
            _list.SetLoading(true, Lang.T("正在检查启动项..."), Lang.T("正在读取注册表、启动文件夹与计划任务"));
            _status = Lang.T("正在读取注册表与服务列表...");
            _bar.Invalidate();
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
                        _list.SetLoading(false, null);
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
            _filter = PendingFilter.Length > 0 ? PendingFilter : "all";
            PendingFilter = "";

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

            MakeChip(Lang.T("全部"), _items.Count, "all", _filter == "all");
            MakeChip(Lang.T("用户启动项"), user, "user", _filter == "user");
            MakeChip(Lang.T("计划任务"), task, "task", _filter == "task");
            MakeChip(Lang.T("自动插件"), plugin, "plugin", _filter == "plugin");
            MakeChip(Lang.T("应用软件服务"), appsvc, "appsvc", _filter == "appsvc");
            MakeChip(Lang.T("系统关键服务"), syssvc, "syssvc", _filter == "syssvc");
            MakeChip(Lang.T("已禁用"), off, "disabled", _filter == "disabled");
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
                ApplyAdvice(it, r);
                r.Note = Lang.T("命令：") + Theme.ShortPath(it.Command, 110);
                r.Tag = it.GroupText;
                r.TagColor = GroupColor(it.Group);
                r.Right = it.Enabled ? Lang.T("已启用") : Lang.T("已禁用");
                r.Alarm = !it.Enabled;
                r.Data = it;
                if (it.Kind.Length == 0 && it.IsService)
                {
                    r.Action = "";
                    r.Disabled = false;
                }
                else
                {
                    r.Action = it.Enabled ? Lang.T("禁用") : Lang.T("启用");
                    r.ActionColor = it.Enabled ? Theme.Warn : Theme.Accent;
                }
                rows.Add(r);
            }
            _list.SetRows(rows);
            int off = 0;
            for (int i = 0; i < _items.Count; i++) if (!_items[i].Enabled) off++;
            _status = Lang.T("当前显示 ") + shown + Lang.T(" 项 · 共 ") + _items.Count + Lang.T(" 项，其中已禁用 ") + off + Lang.T(" 项；停用服务与插件需管理员权限");
            _bar.Invalidate();
        }

        private bool Match(StartupItem it)
        {
            if (_filter == "disabled") return !it.Enabled;
            if (_hideOff && !it.Enabled) return false;
            if (_filter == "all") return true;
            return it.Group == _filter;
        }

        /// <summary>给每项写一句用途说明与“禁用的影响”，只在命中已知特征时下结论，其余给出通用结论。</summary>
        private void ApplyAdvice(StartupItem it, VRow r)
        {
            string n = (it.Name ?? "").ToLowerInvariant();
            string c = (it.Command ?? "").ToLowerInvariant();
            bool isUpdater = Has(n, c, "update", "updater", "upgrade", Lang.T("自动更新"))
                || Has(n, c, "googleupdate", "gupdate", "edgeupdate", "adobeupdater", "ccxprocess", "sunjava")
                || Has(n, c, "updatechecker", "wuauclt", "softwareupdate");
            bool isSecurity = Has(n, c, "defender", "msmpeng", "securityhealth", "antivirus", "360", "huorong", Lang.T("火绒"), Lang.T("安全"))
                || Has(n, c, "kaspersky", "avast", "avg", "nod32", "eset");
            bool isSync = Has(n, c, "onedrive", "dropbox", "baidunetdisk", Lang.T("百度网盘"), Lang.T("阿里云盘"), Lang.T("坚果云"), "sync");
            bool isIme = Has(n, c, "sogou", Lang.T("搜狗"), Lang.T("qq拼音"), "ime", "input");
            bool isAudio = Has(n, c, "audio", "realtek", "wave", "sound");
            bool isGpu = Has(n, c, "amd", "nvidia", "nvsvc", "\\ati\\", "catalyst", "igfx", "intel graphics", Lang.T("显卡"));
            bool isChat = Has(n, c, "wechat", Lang.T("微信"), "qq", "dingtalk", Lang.T("钉钉"), "wxwork", Lang.T("企业微信"), "telegram", "feishu", Lang.T("飞书"));
            bool isMsTask = it.IsTask && (c.Contains("\\microsoft\\windows\\") || c.Contains("microsoft\\windows\\")
                || n.Contains("microsoft") || c.Contains("\\windows\\"));

            bool isActiveSetup = (it.Source ?? "").Contains("Active Setup") || Has(n, c, "chrmstp", "--configure-user-settings", "/firstlogon");

            string text;
            Color color = Theme.Accent;

            if (isActiveSetup)
            {
                text = Lang.T("首次登录的一次性配置项：只在新建用户首次登录时执行，禁用不影响浏览器或程序正常使用");
                color = Theme.Accent;
            }
            else if (it.Group == "syssvc" || it.Group == "sys")
            {
                text = Lang.T("系统组件：禁用可能导致系统功能异常，不建议在此关闭");
                color = Theme.Danger;
            }
            else if (isSecurity)
            {
                text = Lang.T("安全防护组件：禁用会降低实时防护能力，不影响其他程序运行，但不建议禁用");
                color = Theme.Danger;
            }
            else if (isGpu || isAudio)
            {
                text = Lang.T("驱动相关服务：禁用可能影响显示效果或声音输出，不建议禁用，可保留默认");
                color = Theme.Warn;
            }
            else if (isMsTask)
            {
                text = Lang.T("Windows 自带维护任务：禁用会停止自动更新或诊断维护，一般建议保留");
                color = Theme.Warn;
            }
            else if (isUpdater)
            {
                text = Lang.T("只负责自动更新：禁用后程序仍可正常手动使用，只是不再自动升级，可放心禁用");
                color = Theme.Accent;
            }
            else if (isSync)
            {
                text = Lang.T("只负责同步与备份：禁用后不再开机自动同步，手动打开程序仍可正常使用");
                color = Theme.Accent;
            }
            else if (isIme)
            {
                text = Lang.T("输入法预加载：禁用后仍可正常打字，只是首次切换输入法会稍慢");
                color = Theme.Accent;
            }
            else if (isChat)
            {
                text = Lang.T("通讯软件启动项：禁用后不会开机自动登录，手动打开仍可正常收发消息");
                color = Theme.Accent;
            }
            else if (it.Group == "plugin")
            {
                text = Lang.T("插件/右键菜单入口：禁用后对应菜单项消失，不影响程序主体功能");
                color = Theme.Accent;
            }
            else if (it.IsService)
            {
                text = Lang.T("应用后台服务：禁用后该软件的自动更新或托盘功能可能失效，程序主体一般仍可用，按需决定");
                color = Theme.Warn;
            }
            else if (it.IsTask)
            {
                text = Lang.T("计划任务：禁用后不再按计划自动执行，手动使用不受影响");
                color = Theme.Accent;
            }
            else
            {
                text = Lang.T("开机启动项：禁用后不会开机自动运行，程序仍可手动打开，不影响正常使用");
                color = Theme.Accent;
            }

            r.Sub = text;
            r.SubColor = color;
        }

        private bool Has(string name, string command, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                if (k.Length == 0) continue;
                if (name.Contains(k) || command.Contains(k)) return true;
            }
            return false;
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
            Theme.Txt(e.Graphics, Lang.T("禁用只影响开机自启，不会删除程序；注册表项与文件均先备份"), Theme.F(8.5f), Theme.TextDim, 26, 12);
            Theme.Txt(e.Graphics, _status, Theme.F(8f), Theme.Muted, 26, 34);
        }

        private void ToggleSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            StartupItem it = r.Data as StartupItem;
            if (it == null) return;
            if (it.Kind.Length == 0 && it.IsService)
            {
                Dlg.Info(FindForm(), Lang.T("系统服务仅展示"), Lang.T("服务项涉及系统组件，AG Cleaner 不在此处启停。\n如需调整，请在“服务”管理器中操作，或使用系统配置工具。"));
                return;
            }
            string message = "";
            bool ok;
            if (it.Enabled)
            {
                string mode;
                string warn;
                if (it.Kind == "svc")
                {
                    mode = it.Group == "syssvc" ? Lang.T("系统关键服务") : Lang.T("应用软件服务");
                    warn = Lang.T("停用后该服务不再随系统自动启动，依赖它的功能可能失效。\n原始启动方式已记录，可在此页随时还原。");
                }
                else if (it.Kind == "plugin")
                {
                    mode = Lang.T("自动插件");
                    warn = Lang.T("停用后对应的浏览器插件、图标叠加或右键菜单项会消失，不影响程序主体。\n注册表项会先导出备份，可在此页随时还原。");
                }
                else if (it.IsTask)
                {
                    mode = Lang.T("计划任务");
                    warn = Lang.T("禁用只阻止它开机自动运行，不会删除程序本身。原始数据会自动备份，可在此页随时“启用”还原。");
                }
                else if (it.IsFile)
                {
                    mode = Lang.T("启动文件夹项");
                    warn = Lang.T("禁用只阻止它开机自动运行，不会删除程序本身。原始数据会自动备份，可在此页随时“启用”还原。");
                }
                else
                {
                    mode = Lang.T("注册表启动项");
                    warn = Lang.T("禁用只阻止它开机自动运行，不会删除程序本身。原始数据会自动备份，可在此页随时“启用”还原。");
                }
                bool confirm = Dlg.Confirm(FindForm(), Lang.T("禁用") + mode + " " + it.Name + "？",
                    Lang.T("命令：") + Theme.ShortPath(it.Command, 90) + "\n\n" + warn,
                    Lang.T("禁用"), false);
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

