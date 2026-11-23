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
        public DetailForm(CleanItem item)
        {
            Text = Lang.T("详情") + " · " + item.Name;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(660, 520);
            MinimumSize = new Size(460, 320);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f);
            Padding = new Padding(18, 0, 18, 4);

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 86;
            head.BackColor = Theme.Bg;
            Controls.Add(head);

            Label t = new Label();
            t.Text = item.Name;
            t.Font = Theme.F(12.5f, FontStyle.Bold);
            t.ForeColor = Theme.Text;
            t.SetBounds(18, 12, 600, 26);
            t.AutoSize = false;
            head.Controls.Add(t);

            Label s = new Label();
            s.Text = (item.Path.Length > 0 ? item.Path : "") + "    " + Byt.Format(item.Bytes);
            s.ForeColor = Theme.Muted;
            s.Font = Theme.F(9f);
            s.SetBounds(18, 42, 620, 20);
            s.AutoSize = false;
            head.Controls.Add(s);

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Both;
            body.WordWrap = false;
            body.BackColor = Theme.Card;
            body.ForeColor = Theme.Text;
            body.BorderStyle = BorderStyle.None;
            body.Font = Theme.F(9.5f);
            body.Dock = DockStyle.Fill;
            body.Text = Describe(item);
            body.Select(0, 0);
            Controls.Add(body);
            body.BringToFront();

            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 52;
            foot.BackColor = Theme.Bg;
            Controls.Add(foot);

            Btn close = new Btn();
            close.Kind = ButtonKind.Accent;
            close.Text = Lang.T("关闭");
            close.Size = new Size(96, 34);
            close.SetBounds(660 - 18 - 96, 9, 96, 34);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            foot.Controls.Add(close);

            head.Padding = new Padding(0);
            body.Margin = new Padding(18, 0, 18, 0);
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
