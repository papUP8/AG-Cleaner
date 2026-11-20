using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    /// <summary>残留清理页：孤儿目录、无效卸载项、失效快捷方式、失效启动项。</summary>
    public class OrphanPage : PageBase
    {
        public override string PageTitle { get { return "残留清理"; } }
        public override string PageSubtitle { get { return "专治已卸载软件留下的空壳：旧目录、旧注册表项、失效快捷方式；删除可恢复"; } }

        private VList _list;
        private ActionBar _bar;
        private Btn _scanBtn;
        private Btn _allBtn;
        private Btn _invBtn;
        private Btn _cleanBtn;
        private Spinner _spinner;
        private ProgressBar2 _prog;
        private List<Chip> _chips = new List<Chip>();
        private List<OrphanGroup> _groups = new List<OrphanGroup>();
        private string _filter = "all";
        private string _status = "正在查找残留，首次扫描会统计目录体积，需要一会儿";
        private bool _busy;

        public OrphanPage()
        {
            _list = new VList();
            _list.ShowCheck = true;
            _list.RightHeader = "体积";
            _list.EmptyText = "没有发现残留";
            _list.EmptyHint = "干净的系统里通常只剩零星快捷方式，扫描结果为空也是正常的";
            _list.CheckChanged += delegate { UpdateSummary(); };
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            _scanBtn = new Btn();
            _scanBtn.Kind = ButtonKind.Subtle;
            _scanBtn.Text = "重新扫描";
            _scanBtn.Size = new Size(96, 32);
            _scanBtn.Click += delegate { StartScan(); };
            Controls.Add(_scanBtn);

            _allBtn = new Btn();
            _allBtn.Kind = ButtonKind.Link;
            _allBtn.Text = "全选";
            _allBtn.Size = new Size(52, 30);
            _allBtn.Click += delegate { _list.CheckAll(true); UpdateSummary(); };
            Controls.Add(_allBtn);

            _invBtn = new Btn();
            _invBtn.Kind = ButtonKind.Link;
            _invBtn.Text = "反选";
            _invBtn.Size = new Size(52, 30);
            _invBtn.Click += delegate { _list.InvertChecks(); UpdateSummary(); };
            Controls.Add(_invBtn);

            _cleanBtn = new Btn();
            _cleanBtn.Kind = ButtonKind.Accent;
            _cleanBtn.Text = "清理所选";
            _cleanBtn.Size = new Size(128, 38);
            _cleanBtn.Click += delegate { Execute(); };
            _bar.Controls.Add(_cleanBtn);

            _spinner = new Spinner();
            _spinner.BackColor = Theme.Bg;
            _spinner.Visible = false;
            Controls.Add(_spinner);
            _spinner.BringToFront();

            _prog = new ProgressBar2();
            _prog.Visible = false;
            Controls.Add(_prog);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _scanBtn.SetBounds(0, y, 96, 32);
            _allBtn.SetBounds(104, y + 1, 52, 30);
            _invBtn.SetBounds(156, y + 1, 52, 30);
            int cx = 216;
            int cy = y + 2;
            int rows = 1;
            for (int i = 0; i < _chips.Count; i++)
            {
                Graphics g = CreateGraphics();
                int cw = _chips[i].FitWidth(g);
                g.Dispose();
                if (cx + cw > w - 8 && cx > 0)
                {
                    cx = 0;
                    cy += 34;
                    rows++;
                }
                _chips[i].SetBounds(cx, cy, cw, 28);
                cx += cw + 8;
            }
            int listTop = y + 46 + (rows - 1) * 34;
            _bar.SetBounds(0, Height - 58, w, 58);
            if (_prog != null && _prog.Visible)
            {
                _prog.SetBounds(0, listTop, w, 44);
                listTop += 52;
            }
            _list.SetBounds(0, listTop, w, Height - listTop - 92);
            _spinner.SetBounds(w - 46, y + 4, 24, 24);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        public override void OnEnter()
        {
            if (_groups.Count == 0) StartScan();
        }

        public void StartScan()
        {
            if (_busy) return;
            _busy = true;
            _status = "正在扫描...";
            _list.Clear();
            _spinner.Spin(true);
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _prog.Visible = true;
            _prog.BringToFront();
            _prog.SetProgress(0, "准备扫描残留...");
            LayoutChildren();
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<OrphanGroup> result = null;
                string error = null;
                try
                {
                    result = Orphans.Scan(delegate (string msg)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { _status = msg; _bar.Invalidate(); }); }
                        catch (Exception) { }
                    }, delegate (int pc, string msg)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _status = msg;
                                _prog.Visible = true;
                                _prog.SetProgress(pc, msg);
                                _bar.Invalidate();
                            });
                        }
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
                        _prog.Visible = false;
                        LayoutChildren();
                        _scanBtn.Enabled = true;
                        _cleanBtn.Enabled = true;
                        if (result != null)
                        {
                            _groups = result;
                            BuildChips();
                            FillList();
                            int count = 0;
                            long bytes = 0;
                            for (int i = 0; i < _groups.Count; i++)
                            {
                                count += _groups[i].Items.Count;
                                for (int j = 0; j < _groups[i].Items.Count; j++) bytes += _groups[i].Items[j].Bytes;
                            }
                            _status = count == 0 ? "扫描完成：没有发现残留" : "扫描完成：共 " + count + " 项，目录体积 " + Byt.Format(bytes);
                        }
                        else _status = "扫描失败：" + error;
                        _bar.Invalidate();
                        LayoutChildren();
                        UpdateSummary();
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
            int total = 0;
            for (int i = 0; i < _groups.Count; i++) total += _groups[i].Items.Count;
            Chip all = MakeChip("全部", total, "all");
            all.Active = true;
            _chips.Add(all);
            for (int i = 0; i < _groups.Count; i++)
            {
                OrphanGroup g = _groups[i];
                if (g.Items.Count == 0) continue;
                _chips.Add(MakeChip(g.Name, g.Items.Count, g.Id));
            }
            LayoutChildren();
        }

        private Chip MakeChip(string name, int count, string id)
        {
            Chip chip = new Chip();
            chip.Text = name;
            chip.Count = count >= 0 ? count : -1;
            chip.Click += delegate
            {
                _filter = id;
                for (int i = 0; i < _chips.Count; i++)
                {
                    _chips[i].Active = _chips[i] == chip;
                    _chips[i].Invalidate();
                }
                FillList();
                UpdateSummary();
            };
            Controls.Add(chip);
            chip.BringToFront();
            return chip;
        }

        private Color KindColor(string id)
        {
            if (id == "dir") return Theme.Danger;
            if (id == "reg") return Theme.C("#8B7CF6");
            if (id == "lnk") return Theme.Blue;
            if (id == "startup") return Theme.Warn;
            return Theme.Muted;
        }

        private void FillList()
        {
            List<VRow> rows = new List<VRow>();
            for (int i = 0; i < _groups.Count; i++)
            {
                OrphanGroup g = _groups[i];
                if (_filter != "all" && g.Id != _filter) continue;
                for (int j = 0; j < g.Items.Count; j++)
                {
                    OrphanItem it = g.Items[j];
                    VRow r = new VRow();
                    r.Title = it.Name;
                    r.Sub = it.Kind == "dir" ? it.Detail + "  ·  " + Short(it.Target) : it.Detail;
                    r.Right = it.Bytes > 0 ? Byt.Format(it.Bytes) : "—";
                    r.Tag = g.Name;
                    r.TagColor = KindColor(g.Id);
                    r.Checked = false;
                    r.Checkable = true;
                    r.Data = it;
                    if (it.AdminNeeded) r.Sub = r.Sub + "（需要管理员）";
                    rows.Add(r);
                }
            }
            _list.SetRows(rows);
            _list.Invalidate();
        }

        private static string Short(string path)
        {
            if (path == null) return "";
            if (path.Length <= 64) return path;
            return path.Substring(0, 30) + " ... " + path.Substring(path.Length - 30);
        }

        private void Selected(out long bytes, out int count, out int regCount)
        {
            bytes = 0;
            count = 0;
            regCount = 0;
            for (int i = 0; i < _list.VisibleCount; i++)
            {
                VRow r = _list.RowAt(i);
                if (!r.Checked) continue;
                count++;
                OrphanItem it = r.Data as OrphanItem;
                if (it == null) continue;
                bytes += it.Bytes;
                if (it.Kind != "dir") regCount++;
            }
        }

        private void UpdateSummary()
        {
            long b = 0;
            int c = 0, rc = 0;
            Selected(out b, out c, out rc);
            _bar.Invalidate();
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            long b = 0;
            int c = 0, rc = 0;
            Selected(out b, out c, out rc);
            string head = "已选 " + c + " 项";
            if (b > 0) head += " · 目录 " + Byt.Format(b);
            if (rc > 0) head += " · 注册表/快捷方式 " + rc + " 条";
            Theme.Txt(g, head, Theme.F(9.5f, FontStyle.Bold), Theme.Text, 4, 12);
            Theme.Txt(g, _status, Theme.F(8f), Theme.Muted, 4, 34);
            _cleanBtn.SetBounds(_bar.Width - 148, 10, 128, 38);
        }

        private void Execute()
        {
            List<OrphanItem> selected = new List<OrphanItem>();
            for (int i = 0; i < _list.VisibleCount; i++)
            {
                VRow r = _list.RowAt(i);
                if (!r.Checked) continue;
                OrphanItem it = r.Data as OrphanItem;
                if (it != null) selected.Add(it);
            }
            if (selected.Count == 0)
            {
                Dlg.Info(FindForm(), "没有选中的项目", "请先勾选要清理的残留项。默认不勾选，就是为了让你逐条确认。");
                return;
            }

            long b = 0;
            int rc = 0;
            bool admin = false;
            for (int i = 0; i < selected.Count; i++)
            {
                b += selected[i].Bytes;
                if (selected[i].Kind != "dir") rc++;
                if (selected[i].AdminNeeded) admin = true;
            }

            string text = "目录会移入回收站，可随时还原；注册表项会先导出 .reg 备份到配置目录，再删除；启动项会备份后禁用。\n已选 " + selected.Count + " 项（注册表/快捷方式 " + rc + " 条），目录体积约 " + Byt.Format(b) + "。";
            if (admin) text += "\n其中部分项目需要管理员权限，当前不是管理员，可能失败。";
            bool ok = Dlg.Confirm(FindForm(), "确认清理这些残留？", text, "开始清理", true);
            if (!ok) return;

            _busy = true;
            _spinner.Spin(true);
            _scanBtn.Enabled = false;
            _cleanBtn.Enabled = false;
            _status = "正在清理...";
            _prog.Visible = true;
            _prog.BringToFront();
            _prog.SetProgress(0, "准备处理残留...");
            LayoutChildren();
            _bar.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0;
                int errors = 0;
                List<string> log = null;
                try
                {
                    freed = Orphans.Execute(selected, out log, out errors, delegate (int pc, string msg)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _status = msg;
                                _prog.Visible = true;
                                _prog.SetProgress(pc, msg);
                                _bar.Invalidate();
                            });
                        }
                        catch (Exception) { }
                    });
                }
                catch (Exception ex) { log = new List<string>(new string[] { "执行失败：" + ex.Message }); }
                long f = freed;
                List<string> lg = log;
                int err = errors;
                int sel = selected.Count;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _spinner.Spin(false);
                        _prog.Visible = false;
                        LayoutChildren();
                        _cleanBtn.Enabled = true;
                        _status = "已处理 " + sel + " 项，回收 " + Byt.Format(f) + (err > 0 ? "，" + err + " 项失败" : "");
                        _bar.Invalidate();
                        List<string> lines = new List<string>();
                        if (lg != null) { for (int i = 0; i < lg.Count; i++) lines.Add(lg[i]); }
                        string summary = "回收空间 " + Byt.Format(f) + " · 处理 " + sel + " 项 · 明细 " + lines.Count + " 条";
                        string hint = err > 0 ? "有 " + err + " 项未能处理，通常是被进程占用或权限不足，可重启后重试" : "目录已进回收站、注册表已导出 .reg 备份，均可在需要时还原";
                        Dlg.Report(FindForm(), "残留清理完成", summary, lines, hint);
                        _groups.Clear();
                        StartScan();
                    });
                }
                catch (Exception) { }
            });
        }

        private string Join(List<string> list, int max)
        {
            string s = "";
            for (int i = 0; i < list.Count && i < max; i++) s += "· " + list[i] + "\n";
            if (list.Count > max) s += "· 其余 " + (list.Count - max) + " 条已写入操作日志";
            return s;
        }
    }
}
