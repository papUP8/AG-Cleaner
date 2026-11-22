using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AGCleaner.Core;

namespace AGCleaner
{
    public class SettingsPage : PageBase
    {
        public override string PageTitle { get { return Lang.T("设置"); } }
        public override string PageSubtitle { get { return Lang.T("清理偏好、白名单与语言设置；所有配置保存在本地"); } }

        private Card _pref;
        private Card _white;
        private Card _scope;
        private Card _about;
        private Btn _langBtn;
        private Btn _docBtn;
        private List<CheckBox2> _driveChecks = new List<CheckBox2>();

        private Toggle _recycle;
        private Toggle _gpu;
        private Toggle _installers;
        private Btn _days;

        private TextBox _whiteBox;
        private Btn _whiteSave;
        private Btn _whiteDefault;


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


            _scope = new Card();
            _scope.Paint += ScopePaint;
            Controls.Add(_scope);

            _about = new Card();
            _about.Paint += AboutPaint;
            Controls.Add(_about);

            _langBtn = new Btn();
            _langBtn.Kind = ButtonKind.Ghost;
            _langBtn.Size = new Size(110, 28);
            _langBtn.Text = Lang.NativeName(Lang.Cur);
            _langBtn.Click += delegate { OpenLanguageMenu(); };
            Controls.Add(_langBtn);

            _langLabel = new Label();
            _langLabel.BackColor = Theme.Bg;
            _langLabel.ForeColor = Theme.TextDim;
            _langLabel.Font = Theme.F(8.2f);
            _langLabel.TextAlign = ContentAlignment.MiddleRight;
            _langLabel.Text = Lang.T("界面语言 / Language");
            Controls.Add(_langLabel);
            _langLabel.BringToFront();

            _docBtn = new Btn();
            _docBtn.Kind = ButtonKind.Ghost;
            _docBtn.Size = new Size(118, 28);
            _docBtn.Text = Lang.T("说明与许可");
            _docBtn.Click += delegate
            {
                using (DocForm df = new DocForm()) df.ShowDialog(FindForm());
            };
            _about.Controls.Add(_docBtn);

            _recycle = AddToggle(_pref, 0, _settings.UseRecycleBin, delegate (bool v) { _settings.UseRecycleBin = v; _settings.Save(); });
            _gpu = AddToggle(_pref, 1, _settings.CleanGpuShaders, delegate (bool v) { _settings.CleanGpuShaders = v; _settings.Save(); });
            _installers = AddToggle(_pref, 2, _settings.DeleteOldInstallers, delegate (bool v) { _settings.DeleteOldInstallers = v; _settings.Save(); });

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
            _whiteSave.Size = new Size(136, 30);
            _whiteSave.Click += delegate { SaveWhite(); };
            _white.Controls.Add(_whiteSave);

            _whiteDefault = new Btn();
            _whiteDefault.Kind = ButtonKind.Link;
            _whiteDefault.Text = Lang.T("恢复默认");
            _whiteDefault.Size = new Size(112, 28);
            _whiteDefault.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Guard.DefaultWhitelist.Length; i++) sb.AppendLine(Guard.DefaultWhitelist[i]);
                _whiteBox.Text = sb.ToString().Trim();
                SaveWhite();
            };
            _white.Controls.Add(_whiteDefault);



            LoadWhiteBox();
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
            int gap = 10;
            int leftW = (int)(w * 0.56);
            int y = HeaderH + 4;
            int h1 = 250;
            _pref.SetBounds(0, y, leftW, h1);
            _white.SetBounds(leftW + gap, y, w - leftW - gap, h1);

            for (int i = 0; i < _pref.Controls.Count; i++)
            {
                Toggle t = _pref.Controls[i] as Toggle;
                if (t == null) continue;
                int row = (int)t.Tag;
                t.SetBounds(_pref.Width - 62, 52 + row * 44, 46, 24);
            }
            _days.SetBounds(_pref.Width - 96, 188, 76, 28);

            _whiteBox.SetBounds(18, 66, _white.Width - 36, h1 - 120);
            _whiteSave.SetBounds(_white.Width - 154, h1 - 44, 136, 30);
            _whiteDefault.SetBounds(_white.Width - 278, h1 - 43, 112, 28);

            int y2 = y + h1 + gap;
            int hs = 80;
            _scope.SetBounds(0, y2, w, hs);
            for (int i = 0; i < _driveChecks.Count; i++)
            {
                _driveChecks[i].SetBounds(20 + i * 104, 48, 96, 28);
            }

            int y4 = y2 + hs + gap;
            _about.SetBounds(0, y4, w, Math.Min(150, Math.Max(110, Height - y4 - 4)));
            _langBtn.SetBounds(w - 138, 6, 128, 30);
            _langLabel.SetBounds(w - 138 - 156, 9, 150, 24);
            _langLabel.BringToFront();
            _docBtn.SetBounds(_about.Width - 152, 40, 118, 28);
        }

        private Label _langLabel;

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
            string[] titles = new string[] { Lang.T("删除时移入回收站（更安全）"), Lang.T("扫描 GPU 着色器缓存"), Lang.T("扫描下载目录中的旧安装包") };
            string[] descs = new string[]
            {
                Lang.T("关闭则永久删除，缓存类项目默认永久删除更省空间"),
                Lang.T("删除后游戏与应用首次启动需重新编译着色器"),
                Lang.T("仅处理下载目录中 3 天前、大于 5 MB 的 msi / exe"),
                Lang.T("仅处理下载目录中 3 天前、大于 5 MB 的 msi / exe")
            };
            for (int i = 0; i < 3; i++)
            {
                Theme.Txt(g, titles[i], Theme.F(8.8f), Theme.Text, 20, 50 + i * 44);
                Theme.Txt(g, descs[i], Theme.F(7.8f), Theme.Muted, 20, 68 + i * 44);
            }
            Theme.Txt(g, Lang.T("临时文件保留天数"), Theme.F(8.8f), Theme.Text, 20, 186);
            int descW = c.Width - 120;
            if (descW < 120) descW = 120;
            using (StringFormat sf = new StringFormat())
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                Theme.Txt(g, Lang.T("“全部”表示清空临时目录里未被占用的文件；也可以只清若干天前的文件"),
                    Theme.F(7.6f), Theme.Muted, new Rectangle(20, 204, descW, 26), sf);
            }
        }

        private void WhitePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("白名单"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 18, 18);
            Theme.Txt(g, Lang.T("每行一个路径，支持 %LOCALAPPDATA% 等环境变量，命中即跳过"), Theme.F(7.8f), Theme.Muted, 18, 42);
        }


        private void AboutPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Card c = (Card)sender;
            Theme.Txt(g, Lang.T("关于 AG Cleaner"), Theme.F(10.5f, FontStyle.Bold), Theme.Text, 20, 14);
            Theme.Txt(g, "AG Cleaner " + Program.Version + Lang.T("  ·  Windows 图形桌面清理与优化工具"),
                Theme.F(8.2f), Theme.TextDim, 20, 40);
            Theme.Txt(g, Lang.T("许可证：GPL-3.0 · 社区版，代码开源可审计 · 所有配置与日志只保存在本机"),
                Theme.F(7.9f), Theme.Muted, 20, 62);
            
            // 语言标签跟着按钮走，卡片里不再重复
        }

        private void OpenLanguageMenu()
        {
            MainForm mf = FindForm() as MainForm;
            if (mf == null) return;
            if (!LangSwitch.Pick(mf)) return;
            mf.BeginInvoke((Action)delegate { mf.ReloadPages(8); });
        }
    }
}

