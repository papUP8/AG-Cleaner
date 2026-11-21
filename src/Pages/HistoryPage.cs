using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    public class HistoryPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("历史记录"); } }
        public override string PageSubtitle { get { return Lang.T("每次清理、卸载、启动项改动都会写入本地操作日志"); } }

        private VList _list;
        private ActionBar _bar;
        private Btn _refresh;
        private Btn _export;
        private Btn _clear;
        private string _status = "";
        private List<OpLog.Entry> _entries = new List<OpLog.Entry>();

        public HistoryPage()
        {
            _refresh = new Btn();
            _refresh.Kind = ButtonKind.Subtle;
            _refresh.Text = Lang.T("刷新");
            _refresh.Size = new Size(80, 32);
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _export = new Btn();
            _export.Kind = ButtonKind.Ghost;
            _export.Text = Lang.T("导出 CSV");
            _export.Size = new Size(96, 32);
            _export.Click += delegate { Export(); };
            Controls.Add(_export);

            _clear = new Btn();
            _clear.Kind = ButtonKind.Link;
            _clear.Text = Lang.T("清空日志");
            _clear.Size = new Size(80, 30);
            _clear.Click += delegate
            {
                if (Dlg.Confirm(FindForm(), Lang.T("清空操作日志？"), Lang.T("历史记录将被删除，不影响已执行的操作。"), Lang.T("清空"), true))
                {
                    OpLog.Clear();
                    Load();
                }
            };
            Controls.Add(_clear);

            _list = new VList();
            _list.ShowCheck = false;
            _list.RightHeader = Lang.T("释放");
            _list.RowHeight = 52;
            _list.EmptyText = Lang.T("暂无操作记录");
            _list.EmptyHint = Lang.T("执行清理或卸载后，这里会留下审计记录");
            Controls.Add(_list);

            _bar = new ActionBar();
            _bar.Paint += BarPaint;
            Controls.Add(_bar);

            LayoutChildren();
        }

        public void LayoutChildren()
        {
            int w = Width;
            int y = HeaderH + 2;
            _refresh.SetBounds(0, y, 80, 32);
            _export.SetBounds(88, y, 96, 32);
            _clear.SetBounds(186, y + 2, 80, 30);
            _bar.SetBounds(0, Height - 52, w, 52);
            _list.SetBounds(0, y + 44, w, Height - (y + 44) - 72);
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

        private void Load()
        {
            _entries = OpLog.Read(400);
            List<VRow> rows = new List<VRow>();
            long total = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                OpLog.Entry e = _entries[i];
                VRow r = new VRow();
                r.Title = OpLog.ActionLabel(e.Action) + "  ·  " + Theme.ShortPath(e.Path, 62);
                r.Sub = e.Time.ToString("yyyy-MM-dd HH:mm:ss");
                r.Right = e.Bytes > 0 ? Byt.Format(e.Bytes) : "";
                r.Tag = OpLog.ActionLabel(e.Action);
                r.TagColor = e.Action == "clean" || e.Action == "delete" ? Theme.Accent : Theme.Blue;
                rows.Add(r);
                total += e.Bytes;
            }
            _list.SetRows(rows);
            _status = Lang.T("共 ") + _entries.Count + Lang.T(" 条记录 · 累计释放 ") + Byt.Format(total) + Lang.T(" · 日志文件 ") + Store.OpLogFile;
            _bar.Invalidate();
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.Txt(e.Graphics, _status, Theme.F(8.3f), Theme.Muted, 4, 18);
        }

        private void Export()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AGCleaner-history.csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Lang.T("时间,操作,释放字节,路径"));
                for (int i = 0; i < _entries.Count; i++)
                {
                    OpLog.Entry e = _entries[i];
                    sb.AppendLine(e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "," + OpLog.ActionLabel(e.Action) + "," + e.Bytes + ",\"" + e.Path.Replace("\"", "\"\"") + "\"");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Dlg.Info(FindForm(), Lang.T("已导出"), path);
            }
            catch (Exception ex)
            {
                Dlg.Info(FindForm(), Lang.T("导出失败"), ex.Message);
            }
        }
    }
}

