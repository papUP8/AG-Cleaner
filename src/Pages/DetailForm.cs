using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Drawing.Text;
using AGCleaner.Core;

namespace AGCleaner
{
    /// <summary>单项详情：把这一项到底包含哪些文件/子目录/注册表项摊开给用户看，避免误伤。</summary>
    public class DetailForm : Form
    {
        private CleanItem _item;
        private Action<CleanItem, long, long> _onDeleted;

        public DetailForm(CleanItem item) : this(item, null) { }

        /// <summary>onDeleted(条目, 本次实际释放, 删除后仍剩余)：用于详情页里“手动删除此项”后回写清单。</summary>
        public DetailForm(CleanItem item, Action<CleanItem, long, long> onDeleted)
        {
            _item = item;
            _onDeleted = onDeleted;
            Text = Lang.T("详情") + " · " + item.Name;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f);
            Padding = new Padding(18, 0, 18, 4);

            string text = Describe(item);
            Font bodyFont = Theme.F(9.5f);
            int wrapW = 740;
            int textH = TextRenderer.MeasureText(text, bodyFont, new Size(wrapW, 20000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 24;
            // 窗口按内容定高：字不多时一屏放下，不逼用户去拖滚动条；内容太长才出现竖向滚动
            int wantH = 84 + textH + 56 + 8;
            int capH = (int)(Screen.FromControl(this).WorkingArea.Height * 0.86);
            if (wantH > capH) wantH = capH;
            if (wantH < 360) wantH = 360;
            ClientSize = new Size(800, wantH);
            MinimumSize = new Size(560, 360);

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 84;
            head.BackColor = Theme.Bg;
            Controls.Add(head);

            Label t = new Label();
            t.Text = item.Name;
            t.Font = Theme.F(12.5f, FontStyle.Bold);
            t.ForeColor = Theme.Text;
            t.AutoSize = false;
            t.SetBounds(0, 12, head.Width, 28);
            t.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            head.Controls.Add(t);

            Label s = new Label();
            s.Text = (item.Path.Length > 0 ? item.Path : "") + "    " + Byt.Format(item.Bytes);
            s.ForeColor = Theme.Muted;
            s.Font = Theme.F(9f);
            s.AutoSize = false;
            s.SetBounds(0, 46, head.Width, 20);
            s.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            head.Controls.Add(s);
            head.Resize += delegate
            {
                t.Width = head.ClientSize.Width;
                s.Width = head.ClientSize.Width;
            };

            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 56;
            foot.BackColor = Theme.Bg;
            Controls.Add(foot);

            Btn close = new Btn();
            close.Kind = ButtonKind.Accent;
            close.Text = Lang.T("关闭");
            close.Size = new Size(96, 34);
            close.Dock = DockStyle.Right;
            close.Click += delegate { Close(); };
            // 靠 Dock 贴右侧，随窗口宽度自适应，不会再被右边距切掉一半
            foot.Padding = new Padding(0, 11, 8, 11);
            foot.Controls.Add(close);

            // 既然这一项能扫出来，就应该能单独删掉：给它一个“删除此项”，不必整轮重扫
            bool canDelete = !item.Recycle &&
                (item.Kind == CleanKind.OrphanGroup || Directory.Exists(item.Path) || File.Exists(item.Path));
            if (canDelete)
            {
                Btn del = new Btn();
                del.Kind = ButtonKind.Danger;
                del.Text = Lang.T("删除此项");
                del.Size = new Size(104, 34);
                del.Dock = DockStyle.Right;
                del.Click += delegate { DeleteThis(); };
                foot.Controls.Add(del);
            }

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            // 自动换行 + 只保留竖向滚动条：长句不再被横向截断，也不用左右拖
            body.ScrollBars = ScrollBars.Vertical;
            body.WordWrap = true;
            body.BackColor = Theme.Card;
            body.ForeColor = Theme.Text;
            body.BorderStyle = BorderStyle.None;
            body.Font = bodyFont;
            body.Dock = DockStyle.Fill;
            body.Text = text;
            body.Select(0, 0);
            Controls.Add(body);
            body.BringToFront();
        }

        private void DeleteThis()
        {
            CleanItem it = _item;
            if (it == null) return;
            long size = 0;
            if (it.Kind == CleanKind.OrphanGroup) size = it.Bytes;
            else try { size = FileUtil.SizeOf(it.Path); } catch (Exception) { }
            string body = Lang.T("将永久删除下列内容，不会进回收站，删掉无法恢复：\n\n") + it.Path +
                "\n\n" + Lang.T("当前体积 ") + Byt.Format(size);
            if (!Dlg.Confirm(this, Lang.T("删除 ") + it.Name + Lang.T(" ？"), body, Lang.T("删除"), true)) return;

            long freed = 0;
            int errors = 0;
            List<CleanItem> one = new List<CleanItem>();
            one.Add(it);
            try
            {
                List<string> log = null;
                freed = CleanEngine.Execute(one, out log, out errors);
            }
            catch (Exception ex)
            {
                Dlg.Info(this, Lang.T("删除失败"), ex.Message);
                return;
            }
            long remain = 0;
            if (it.Kind == CleanKind.OrphanGroup) remain = Math.Max(0, it.Bytes - freed);
            else
            {
                try
                {
                    if (Directory.Exists(it.Path) || File.Exists(it.Path)) remain = FileUtil.SizeOf(it.Path);
                }
                catch (Exception) { }
            }
            if (_onDeleted != null) _onDeleted(it, freed, remain);

            string msg;
            if (remain <= 0) msg = Lang.T("已删除完成，本次释放 ") + Byt.Format(freed) + "。";
            else
            {
                string who = CleanEngine.LastSkips.Count > 0 ? CleanEngine.LastSkips[0].Who : "";
                msg = Lang.T("本次释放 ") + Byt.Format(freed) + Lang.T("，还有 ") + Byt.Format(remain) + Lang.T(" 没删掉");
                msg += who.Length > 0 ? Lang.T("，这些文件正被 ") + who + Lang.T(" 占用。") : Lang.T("，文件仍被占用或当前账户没有删除权限。");
                msg += "\n\n" + Lang.T("把这个程序退出（或改用管理员身份运行）后再删一次，这部分就能清干净。");
            }
            Dlg.Info(this, Lang.T("删除此项"), msg);
            Close();
        }

        private static string CatName(string id)
        {
            if (id == "temp") return Lang.T("临时文件");
            if (id == "browser") return Lang.T("浏览器缓存");
            if (id == "apps") return Lang.T("应用缓存");
            if (id == "traces") return Lang.T("系统痕迹");
            if (id == "usage") return Lang.T("使用痕迹");
            if (id == "sys") return Lang.T("系统缓存与日志");
            if (id == "deep") return Lang.T("深度清理");
            if (id == "gpu") return Lang.T("着色器");
            if (id == "logs") return Lang.T("日志与错误报告");
            if (id == "installer") return Lang.T("旧安装包");
            if (id == "projects") return Lang.T("项目产物");
            if (id == "recycle") return Lang.T("回收站");
            if (id == "residual") return Lang.T("卸载残留");
            return id;
        }

        private static string Describe(CleanItem item)
        {
            List<string> lines = new List<string>();
            lines.Add(Lang.T("分类：") + CatName(item.CategoryId));
            lines.Add(Lang.T("完整路径：") + item.Path);
            lines.Add(Lang.T("体积：") + Byt.Format(item.Bytes) + "   " + Lang.T("文件数：") + item.Files);
            if (item.Impact.Length > 0)
            {
                lines.Add("");
                lines.Add(Lang.T("删除后的影响："));
                string[] ip = item.Impact.Split('\n');
                for (int i = 0; i < ip.Length; i++) lines.Add("  " + ip[i]);
            }
            if (item.Note.Length > 0) lines.Add(Lang.T("说明：") + item.Note);

            if (item.Kind == CleanKind.OrphanGroup)
            {
                OrphanGroup g = item.Data as OrphanGroup;
                if (g != null)
                {
                    lines.Add("");
                    lines.Add(Lang.T("这一组包含 ") + g.Items.Count + Lang.T(" 个残留项，全部列在下面："));
                    lines.Add("");
                    for (int i = 0; i < g.Items.Count; i++)
                    {
                        OrphanItem o = g.Items[i];
                        string size = o.Bytes > 0 ? Byt.Format(o.Bytes) : "-";
                        lines.Add("  " + (i + 1) + ". " + o.Name + "   " + size);
                        lines.Add("     " + (o.Target.Length > 0 ? o.Target : o.KeyPath));
                        if (o.Detail.Length > 0) lines.Add("     " + o.Detail);
                    }
                }
            }
            else if (Directory.Exists(item.Path))
            {
                lines.Add("");
                lines.Add(Lang.T("目录内容（最多显示 20 项）："));
                lines.Add("");
                try
                {
                    List<string> names = new List<string>();
                    List<long> sizes = new List<long>();
                    List<bool> dirs = new List<bool>();
                    DirectoryInfo di = new DirectoryInfo(item.Path);
                    int shown = 0;
                    foreach (FileSystemInfo fsi in di.GetFileSystemInfos())
                    {
                        if (shown >= 20) break;
                        shown++;
                        bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
                        long sz = 0;
                        if (!isDir) { try { sz = ((FileInfo)fsi).Length; } catch (Exception) { } }
                        names.Add(fsi.Name);
                        sizes.Add(sz);
                        dirs.Add(isDir);
                    }
                    int total = names.Count;
                    for (int i = 0; i < total; i++)
                    {
                        string tag = dirs[i] ? Lang.T("[目录] ") : "       ";
                        string size = dirs[i] ? "" : Byt.Format(sizes[i]);
                        lines.Add("  " + tag + names[i] + "   " + size);
                    }
                    if (total >= 20) lines.Add("  " + Lang.T("…（仅显示前 20 项，共 ") + total + Lang.T(" 项以上）"));
                }
                catch (Exception ex)
                {
                    lines.Add(Lang.T("读取目录失败：") + ex.Message);
                }
            }
            else if (item.Kind == CleanKind.FileItself || item.Kind == CleanKind.OldFilesExt)
            {
                lines.Add("");
                lines.Add(Lang.T("这是一个单个文件，清理时会被直接删除，不会进回收站；删除路径会写进操作日志，可在历史记录里核对。"));
            }
            return string.Join("\r\n", lines.ToArray());
        }
    }
}
