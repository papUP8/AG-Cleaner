using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CCDesk.Core;

namespace CCDesk
{
    public class SettingsPage : PageBase
    {
        public override string PageTitle { get { return "设置"; } }
        public override string PageSubtitle { get { return "清理偏好、白名单与项目目录；所有配置保存在本地"; } }

        private Card _pref;
        private Card _white;
        private Card _proj;
        private Card _about;

        private Toggle _recycle;
        private Toggle _gpu;
        private Toggle _projects;
        private Toggle _installers;
        private Btn _days;

        private TextBox _whiteBox;
        private Btn _whiteSave;
        private Btn _whiteDefault;

        private TextBox _projBox;
        private Btn _projSave;

        private Settings _settings;

        public SettingsPage()
        {
            _settings = Settings.Load();

            _pref = new Card();
            _pref.Paint += PrefPaint;
            Controls.Add(_pref);

            _white = new Card();
            _white.Paint += WhitePaint;
            Controls.Add(_white);

            _proj = new Card();
            _proj.Paint += ProjPaint;
            Controls.Add(_proj);

            _about = new Card();
            _about.Paint += AboutPaint;
            Controls.Add(_about);

            _recycle = AddToggle(_pref, 0, _settings.UseRecycleBin, delegate (bool v) { _settings.UseRecycleBin = v; _settings.Save(); });
            _gpu = AddToggle(_pref, 1, _settings.CleanGpuShaders, delegate (bool v) { _settings.CleanGpuShaders = v; _settings.Save(); });
            _projects = AddToggle(_pref, 2, _settings.CleanProjectArtifacts, delegate (bool v) { _settings.CleanProjectArtifacts = v; _settings.Save(); });
            _installers = AddToggle(_pref, 3, _settings.DeleteOldInstallers, delegate (bool v) { _settings.DeleteOldInstallers = v; _settings.Save(); });

            _days = new Btn();
            _days.Kind = ButtonKind.Subtle;
            _days.Text = DayText(_settings.TempDays);
            _days.Size = new Size(96, 28);
            _days.Click += delegate
            {
                int[] steps = new int[] { 0, 1, 3, 7, 14, 30 };
                int idx = 0;
                for (int i = 0; i < steps.Length; i++) if (steps[i] == _settings.TempDays) idx = i;
                idx = (idx + 1) % steps.Length;
                _settings.TempDays = steps[idx];
                _days.Text = DayText(_settings.TempDays);
                _days.Invalidate();
                _settings.Save();
            };
            _pref.Controls.Add(_days);

            _whiteBox = MakeEditor(_white);
            _white.Controls.Add(_whiteBox);

            _whiteSave = new Btn();
            _whiteSave.Kind = ButtonKind.Ghost;
            _whiteSave.Text = "保存白名单";
            _whiteSave.Size = new Size(100, 30);
            _whiteSave.Click += delegate { SaveWhite(); };
            _white.Controls.Add(_whiteSave);

            _whiteDefault = new Btn();
            _whiteDefault.Kind = ButtonKind.Link;
            _whiteDefault.Text = "恢复默认";
            _whiteDefault.Size = new Size(76, 28);
            _whiteDefault.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Guard.DefaultWhitelist.Length; i++) sb.AppendLine(Guard.DefaultWhitelist[i]);
                _whiteBox.Text = sb.ToString().Trim();
                SaveWhite();
            };
            _white.Controls.Add(_whiteDefault);

            _projBox = MakeEditor(_proj);
            _proj.Controls.Add(_projBox);

            _projSave = new Btn();
            _projSave.Kind = ButtonKind.Ghost;
            _projSave.Text = "保存目录";
            _projSave.Size = new Size(100, 30);
            _projSave.Click += delegate { SaveProjects(); };
            _proj.Controls.Add(_projSave);

            LoadWhiteBox();
            LoadProjectBox();
            LayoutChildren();
        }

        private static string DayText(int days)
        {
            return days <= 0 ? "全部" : days + " 天";
        }

        private Toggle AddToggle(Card card, int row, bool value, Action<bool> onChange)
        {
            Toggle t = new Toggle();
            t.Checked = value;
            t.Tag = row;
            t.Changed += delegate
            {
                onChange(t.Checked);
            };
            card.Controls.Add(t);
            return t;
        }

        private TextBox MakeEditor(Card parent)
        {
            TextBox box = new TextBox();
            box.Multiline = true;
            box.ScrollBars = ScrollBars.None;
            box.WordWrap = false;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.CardSoft;
            box.ForeColor = Theme.TextDim;
            box.Font = Theme.Mono(8.3f);
            box.AcceptsReturn = true;
            return box;
        }

        private void LayoutChildren()
        {
            int w = Width;
            int gap = 14;
            int leftW = (int)(w * 0.56);
            int y = HeaderH + 4;
            int h1 = 270;
            _pref.SetBounds(0, y, leftW, h1);
            _white.SetBounds(leftW + gap, y, w - leftW - gap, h1);

            for (int i = 0; i < _pref.Controls.Count; i++)
            {
                Toggle t = _pref.Controls[i] as Toggle;
                if (t == null) continue;
                int row = (int)t.Tag;
                t.SetBounds(_pref.Width - 62, 54 + row * 46, 46, 24);
            }
            _days.SetBounds(_pref.Width - 96, 228, 76, 28);

            _whiteBox.SetBounds(18, 66, _white.Width - 36, h1 - 120);
            _whiteSave.SetBounds(_white.Width - 118, h1 - 44, 100, 30);
            _whiteDefault.SetBounds(_white.Width - 202, h1 - 43, 76, 28);

            int y2 = y + h1 + gap;
            int h2 = 152;
            _proj.SetBounds(0, y2, w, h2);
            _projBox.SetBounds(18, 60, w - 140, h2 - 78);
            _projSave.SetBounds(w - 118, h2 - 44, 100, 30);

            int y3 = y2 + h2 + gap;
            _about.SetBounds(0, y3, w, Math.Max(90, Height - y3 - 4));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LoadWhiteBox()
        {
            List<string> list = Guard.WhiteList();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++) sb.AppendLine(list[i]);
            _whiteBox.Text = sb.ToString().Trim();
        }

        private void LoadProjectBox()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _settings.ProjectDirs.Count; i++) sb.AppendLine(_settings.ProjectDirs[i]);
            _projBox.Text = sb.ToString().Trim();
        }

        private void SaveWhite()
        {
            List<string> list = new List<string>();
            string[] lines = _whiteBox.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length > 0) list.Add(t);
            }
            Guard.SaveWhiteList(list);
            Dlg.Info(FindForm(), "已保存", "白名单共 " + list.Count + " 条，命中的路径不会被清理。");
        }

        private void SaveProjects()
        {
            List<string> dirs = new List<string>();
            string[] lines = _projBox.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0) continue;
                if (Directory.Exists(Environment.ExpandEnvironmentVariables(t))) dirs.Add(t);
            }
            _settings.ProjectDirs = dirs;
            _settings.Save();
            Dlg.Info(FindForm(), "已保存", "项目扫描目录共 " + dirs.Count + " 个。");
        }

        private void PrefPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, "清理偏好", Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 18);
            string[] titles = new string[] { "删除时移入回收站（更安全）", "扫描 GPU 着色器缓存", "扫描项目构建产物", "扫描下载目录中的旧安装包" };
            string[] descs = new string[]
            {
                "关闭则永久删除，缓存类项目默认永久删除更省空间",
                "删除后游戏与应用首次启动需重新编译着色器",
                "扫描 node_modules、target、dist 等可重建目录",
                "仅处理 14 天前且大于 5 MB 的 msi / exe"
            };
            for (int i = 0; i < 4; i++)
            {
                Theme.Txt(g, titles[i], Theme.F(8.8f), Theme.Text, 20, 52 + i * 46);
                Theme.Txt(g, descs[i], Theme.F(7.8f), Theme.Muted, 20, 70 + i * 46);
            }
            Theme.Txt(g, "临时文件保留天数", Theme.F(8.8f), Theme.Text, 20, 230);
            Theme.Txt(g, "“全部”表示清空临时目录里未被占用的文件；也可以只清若干天前的文件", Theme.F(7.6f), Theme.Muted, 20, 250);
        }

        private void WhitePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, "白名单", Theme.F(10.5f, FontStyle.Bold), Theme.Text, 18, 18);
            Theme.Txt(g, "每行一个路径，支持 %LOCALAPPDATA% 等环境变量，命中即跳过", Theme.F(7.8f), Theme.Muted, 18, 42);
        }

        private void ProjPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Theme.Txt(g, "项目扫描目录", Theme.F(10.5f, FontStyle.Bold), Theme.Text, 18, 16);
            Theme.Txt(g, "每行一个目录，用于查找可重建的构建产物（需开启上方开关）", Theme.F(7.8f), Theme.Muted, 18, 40);
        }

        private void AboutPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, "关于 CCDesk", Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 14);
            Theme.Txt(g, "CCDesk " + Program.Version + "  ·  Windows 图形桌面清理与优化工具",
                Theme.F(8.2f), Theme.TextDim, 20, 40);
            Theme.Txt(g, "许可证：GPL-3.0 · 社区版，代码开源可审计 · 所有配置与日志只保存在本机",
                Theme.F(7.9f), Theme.Muted, 20, 62);
            Theme.Txt(g, "配置目录：" + Store.RootDir, Theme.F(7.6f), Theme.Hint, 20, 84);
        }
    }
}

