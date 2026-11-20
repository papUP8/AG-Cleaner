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
        public override string PageTitle { get { return Lang.T("设置"); } }
        public override string PageSubtitle { get { return Lang.T("清理偏好、白名单与项目目录；所有配置保存在本地"); } }

        private Card _pref;
        private Card _white;
        private Card _scope;
        private Card _proj;
        private Card _about;
        private Btn _langBtn;
        private List<CheckBox2> _driveChecks = new List<CheckBox2>();

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

            _scope = new Card();
            _scope.Paint += ScopePaint;
            Controls.Add(_scope);

            _about = new Card();
            _about.Paint += AboutPaint;
            Controls.Add(_about);

            _langBtn = new Btn();
            _langBtn.Kind = ButtonKind.Ghost;
            _langBtn.Size = new Size(110, 28);
            _langBtn.Text = Lang.Cur == "en" ? Lang.T("中文") : "English";
            _langBtn.Click += delegate { ToggleLanguage(); };
            _about.Controls.Add(_langBtn);

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
            _whiteSave.Text = Lang.T("保存白名单");
            _whiteSave.Size = new Size(100, 30);
            _whiteSave.Click += delegate { SaveWhite(); };
            _white.Controls.Add(_whiteSave);

            _whiteDefault = new Btn();
            _whiteDefault.Kind = ButtonKind.Link;
            _whiteDefault.Text = Lang.T("恢复默认");
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
            _projSave.Text = Lang.T("保存目录");
            _projSave.Size = new Size(100, 30);
            _projSave.Click += delegate { SaveProjects(); };
            _proj.Controls.Add(_projSave);

            LoadWhiteBox();
            LoadProjectBox();
            BuildDriveChecks();
            LayoutChildren();
        }

        private static string DayText(int days)
        {
            return days <= 0 ? Lang.T("全部") : days + Lang.T(" 天");
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

        private void BuildDriveChecks()
        {
            for (int i = 0; i < _driveChecks.Count; i++) _scope.Controls.Remove(_driveChecks[i]);
            _driveChecks.Clear();

            List<string> drives = Disk.Drives();
            string sel = _settings.ScanDrives ?? "";
            bool all = sel.Trim().Length == 0;
            for (int i = 0; i < drives.Count; i++)
            {
                CheckBox2 c = new CheckBox2();
                c.Text = drives[i].TrimEnd('\\');
                c.Size = new Size(96, 28);
                bool on = all;
                if (!all)
                {
                    string letter = drives[i].TrimEnd('\\').ToUpperInvariant();
                    string[] parts = sel.Split(',');
                    for (int j = 0; j < parts.Length; j++) if (parts[j].Trim().ToUpperInvariant() == letter) on = true;
                }
                c.Checked = on;
                c.Changed += delegate { SaveDrives(); };
                _scope.Controls.Add(c);
                _driveChecks.Add(c);
            }
        }

        private void SaveDrives()
        {
            List<string> picked = new List<string>();
            for (int i = 0; i < _driveChecks.Count; i++)
            {
                if (!_driveChecks[i].Checked) continue;
                picked.Add(_driveChecks[i].Text.ToUpperInvariant());
            }
            // 全部勾选（或一个都不勾）都按“全部固定盘”处理
            if (picked.Count == 0 || picked.Count == _driveChecks.Count) _settings.ScanDrives = "";
            else _settings.ScanDrives = string.Join(",", picked.ToArray());
            _settings.Save();
        }

        private void LayoutChildren()
        {
            int w = Width;
            int gap = 12;
            int leftW = (int)(w * 0.56);
            int y = HeaderH + 4;
            int h1 = 256;
            _pref.SetBounds(0, y, leftW, h1);
            _white.SetBounds(leftW + gap, y, w - leftW - gap, h1);

            for (int i = 0; i < _pref.Controls.Count; i++)
            {
                Toggle t = _pref.Controls[i] as Toggle;
                if (t == null) continue;
                int row = (int)t.Tag;
                t.SetBounds(_pref.Width - 62, 54 + row * 46, 46, 24);
            }
            _days.SetBounds(_pref.Width - 96, 226, 76, 28);

            _whiteBox.SetBounds(18, 66, _white.Width - 36, h1 - 120);
            _whiteSave.SetBounds(_white.Width - 118, h1 - 44, 100, 30);
            _whiteDefault.SetBounds(_white.Width - 202, h1 - 43, 76, 28);

            int y2 = y + h1 + gap;
            int hs = 80;
            _scope.SetBounds(0, y2, w, hs);
            for (int i = 0; i < _driveChecks.Count; i++)
            {
                _driveChecks[i].SetBounds(20 + i * 104, 48, 96, 28);
            }
            int y3 = y2 + hs + gap;
            int h2 = 120;
            _proj.SetBounds(0, y3, w, h2);
            _projBox.SetBounds(18, 56, w - 140, h2 - 70);
            _projSave.SetBounds(w - 118, h2 - 42, 100, 30);

            int y4 = y3 + h2 + gap;
            _about.SetBounds(0, y4, w, Math.Max(86, Height - y4 - 4));
            _langBtn.SetBounds(_about.Width - 130, 12, 110, 28);
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
            Dlg.Info(FindForm(), Lang.T("已保存"), Lang.T("白名单共 ") + list.Count + Lang.T(" 条，命中的路径不会被清理。"));
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
            Dlg.Info(FindForm(), Lang.T("已保存"), Lang.T("项目扫描目录共 ") + dirs.Count + Lang.T(" 个。"));
        }

        private void ScopePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("残留扫描范围"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 14);
            Theme.Txt(g, Lang.T("勾选要扫描的磁盘（只影响“残留清理”）；全部勾选或全部取消都表示全部固定盘"),
                Theme.F(7.8f), Theme.Muted, 20, 33);
        }

        private void PrefPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("清理偏好"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 18);
            string[] titles = new string[] { Lang.T("删除时移入回收站（更安全）"), Lang.T("扫描 GPU 着色器缓存"), Lang.T("扫描项目构建产物"), Lang.T("扫描下载目录中的旧安装包") };
            string[] descs = new string[]
            {
                Lang.T("关闭则永久删除，缓存类项目默认永久删除更省空间"),
                Lang.T("删除后游戏与应用首次启动需重新编译着色器"),
                Lang.T("扫描 node_modules、target、dist 等可重建目录"),
                Lang.T("仅处理 14 天前且大于 5 MB 的 msi / exe")
            };
            for (int i = 0; i < 4; i++)
            {
                Theme.Txt(g, titles[i], Theme.F(8.8f), Theme.Text, 20, 52 + i * 46);
                Theme.Txt(g, descs[i], Theme.F(7.8f), Theme.Muted, 20, 70 + i * 46);
            }
            Theme.Txt(g, Lang.T("临时文件保留天数"), Theme.F(8.8f), Theme.Text, 20, 224);
            Theme.Txt(g, Lang.T("“全部”表示清空临时目录里未被占用的文件；也可以只清若干天前的文件"), Theme.F(7.6f), Theme.Muted, 20, 242);
        }

        private void WhitePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("白名单"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 18, 18);
            Theme.Txt(g, Lang.T("每行一个路径，支持 %LOCALAPPDATA% 等环境变量，命中即跳过"), Theme.F(7.8f), Theme.Muted, 18, 42);
        }

        private void ProjPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Theme.Txt(g, Lang.T("项目扫描目录"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 18, 16);
            Theme.Txt(g, Lang.T("每行一个目录，用于查找可重建的构建产物（需开启上方开关）"), Theme.F(7.8f), Theme.Muted, 18, 40);
        }

        private void AboutPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("关于 CC-Desk"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 14);
            Theme.Txt(g, "CC-Desk " + Program.Version + Lang.T("  ·  Windows 图形桌面清理与优化工具"),
                Theme.F(8.2f), Theme.TextDim, 20, 40);
            Theme.Txt(g, Lang.T("许可证：GPL-3.0 · 社区版，代码开源可审计 · 所有配置与日志只保存在本机"),
                Theme.F(7.9f), Theme.Muted, 20, 62);
            Theme.Txt(g, Lang.T("配置目录：") + Store.RootDir, Theme.F(7.6f), Theme.Hint, 20, 84);
            Theme.Txt(g, Lang.T("界面语言 / Language"), Theme.F(8.2f), Theme.TextDim, c.Width - 268, 18);
        }

        private void ToggleLanguage()
        {
            string next = Lang.Cur == "en" ? "zh" : "en";
            Lang.Set(next);
            _settings.Language = next;
            _settings.Save();
            MainForm mf = FindForm() as MainForm;
            if (mf != null) mf.ReloadPages(8);
        }
    }
}

