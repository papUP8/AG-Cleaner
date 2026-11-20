using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    public class UninstallPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("应用卸载"); } }
        public override string PageSubtitle { get { return Lang.T("按体积排序已安装程序，调用官方卸载器并清理残留目录"); } }

        private SearchBox _search;
        private Btn _refresh;
        private VList _list;
        private ActionBar _bar;
        private List<App> _apps = new List<App>();
        private App _selected;
        private bool _busy;
        private string _status = Lang.T("点击“刷新列表”读取已安装程序");
        private Spinner _spinner;

        public UninstallPage()
        {
            _search = new SearchBox();
            _search.Placeholder = Lang.T("搜索应用名称或发行商...");
            _search.Input.TextChanged += delegate { FillList(); };
            Controls.Add(_search);

            _refresh = new Btn();
            _refresh.Kind = ButtonKind.Subtle;
            _refresh.Text = Lang.T("刷新列表");
            _refresh.Size = new Size(96, 34);
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = Lang.T("占用");
            _list.RowHeight = 60;
            _list.EmptyText = Lang.T("尚未读取应用列表");
            _list.EmptyHint = Lang.T("点击右上角“刷新列表”开始");
            _list.ActionClicked += delegate { UninstallSelected(); };
            _list.RowActivated += delegate { UninstallSelected(); };
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
            _search.SetBounds(0, y, 320, 34);
            _refresh.SetBounds(w - 96, y, 96, 34);
            _bar.SetBounds(0, Height - 58, w, 58);
            _list.SetBounds(0, y + 46, w, Height - (y + 46) - 72);
            _spinner.SetBounds(w - 140, y - 6, 44, 44);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            if (_apps.Count == 0) Load();
        }

        public void Load()
        {
            if (_busy) return;
            _busy = true;
            _spinner.Spin(true);
            _status = Lang.T("正在读取注册表中的已安装程序...");
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<App> list = null;
                string error = null;
                try
                {
                    list = Apps.List(delegate (string msg)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { _status = msg; _bar.Invalidate(); }); }
                        catch (Exception) { }
                    });
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        if (list != null)
                        {
                            _apps = list;
                            FillList();
                            _status = Lang.T("共 ") + list.Count + Lang.T(" 个已安装程序");
                        }
                        else _status = Lang.T("读取失败：") + error;
                        _bar.Invalidate();
                    });
                }
                catch (Exception) { }
            });
        }

        private void FillList()
        {
            string q = _search.Text2.Trim().ToLowerInvariant();
            List<VRow> rows = new List<VRow>();
            for (int i = 0; i < _apps.Count; i++)
            {
                App a = _apps[i];
                if (q.Length > 0)
                {
                    if (!a.Name.ToLowerInvariant().Contains(q) && !a.Publisher.ToLowerInvariant().Contains(q)) continue;
                }
                VRow r = new VRow();
                r.Title = a.Name;
                string sub = a.Publisher.Length > 0 ? a.Publisher : Lang.T("未知发行商");
                if (a.Version.Length > 0) sub += "  ·  " + a.Version;
                if (a.Location.Length > 0) sub += "  ·  " + Theme.ShortPath(a.Location, 60);
                r.Sub = sub;
                r.Right = a.SizeText;
                r.Tag = a.Machine ? Lang.T("系统级") : Lang.T("用户级");
                r.TagColor = a.Machine ? Theme.Blue : Theme.Accent;
                r.Action = Lang.T("卸载");
                r.ActionColor = Theme.Danger;
                r.Data = a;
                rows.Add(r);
            }
            _list.SetRows(rows);
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.Txt(e.Graphics, _status, Theme.F(8.5f), Theme.Muted, 4, 20);
            if (_selected != null)
            {
                Theme.Txt(e.Graphics, Lang.T("已选择：") + _selected.Name, Theme.F(8.5f), Theme.TextDim, 4, 40);
            }
        }

        private void UninstallSelected()
        {
            VRow r = _list.SelectedRow;
            if (r == null) return;
            App a = r.Data as App;
            if (a == null) return;
            _selected = a;
            _bar.Invalidate();

            bool ok = Dlg.Confirm(FindForm(), Lang.T("卸载 ") + a.Name + "？",
                Lang.T("将调用该应用自带的卸载器：\n") + Theme.ShortPath(a.Uninstall, 120) +
                Lang.T("\n\n卸载器结束后，可继续扫描并清理残留目录（移入回收站）。") + (a.Machine && !Native.IsAdmin() ? Lang.T("\n\n系统级程序会请求管理员权限。") : ""),
                Lang.T("开始卸载"), true);
            if (!ok) return;

            _busy = true;
            _spinner.Spin(true);
            _status = Lang.T("正在卸载 ") + a.Name + Lang.T("（请在弹出的卸载窗口中完成操作）...");
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string message = "";
                bool done = false;
                try { done = Apps.RunUninstaller(a, false, out message); }
                catch (Exception ex) { message = ex.Message; }
                string m = message;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _status = m;
                        _bar.Invalidate();
                        Load();
                        AfterUninstall(a);
                    });
                }
                catch (Exception) { }
            });
        }

        private void AfterUninstall(App a)
        {
            List<CleanItem> left = Apps.FindLeftovers(a, null);
            if (left.Count == 0)
            {
                Dlg.Info(FindForm(), Lang.T("卸载完成"), Lang.T("没有发现残留目录。"));
                return;
            }
            long bytes = 0;
            string detail = Lang.T("发现 ") + left.Count + Lang.T(" 处残留，可移入回收站：\n");
            for (int i = 0; i < left.Count && i < 6; i++)
            {
                detail += "· " + Theme.ShortPath(left[i].Path, 70) + "   " + Byt.Format(left[i].Bytes) + "\n";
                bytes += left[i].Bytes;
            }
            if (left.Count > 6) detail += Lang.T("· 其余 ") + (left.Count - 6) + Lang.T(" 处");
            detail += Lang.T("\n合计 ") + Byt.Format(bytes);
            bool ok = Dlg.Confirm(FindForm(), Lang.T("清理残留文件？"), detail, Lang.T("移入回收站"), false);
            if (!ok) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                int errors = 0;
                List<string> log = null;
                long freed = CleanEngine.Execute(left, out log, out errors);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Dlg.Info(FindForm(), Lang.T("残留清理完成"), Lang.T("释放 ") + Byt.Format(freed) + (errors > 0 ? "，" + errors + Lang.T(" 项失败") : ""));
                        OpLog.Write("leftover", a.Name, freed);
                        _status = Lang.T("残留已清理 ") + Byt.Format(freed);
                        _bar.Invalidate();
                        Load();
                    });
                }
                catch (Exception) { }
            });
        }
    }
}


