using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AGCleaner
{
    public static class Program
    {
        public static string Version = "1.6.1";

        private static Icon _appIcon;

        /// <summary>窗口与任务栏用的图标：优先取编译进 exe 的多尺寸 ico，取不到再退回 exe 自身图标。</summary>
        public static Icon AppIcon()
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                {
                    if (s != null) _appIcon = new Icon(s);
                }
            }
            catch (Exception) { }
            if (_appIcon == null)
            {
                try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch (Exception) { }
            }
            if (_appIcon == null) _appIcon = SystemIcons.Application;
            return _appIcon;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.AddMessageFilter(new WheelRouter());

            Core.Settings startSettings = Core.Settings.Load();
            Lang.Set(startSettings.Language);
            Theme.ApplyMode(startSettings.ThemeMode, Theme.SystemLight());

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                ReportCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                ReportCrash(e.ExceptionObject as Exception);
            };

            if (args.Length >= 1 && args[0] == "--selftest")
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                string outFile = args.Length >= 2 ? args[1] : "selftest.txt";
                try
                {
                    string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string temp = Environment.GetEnvironmentVariable("TEMP");
                    sb.AppendLine("guard windows=" + Core.Guard.CanDelete("C:\\Windows\\System32", true));
                    sb.AppendLine("guard profile=" + Core.Guard.CanDelete(profile, true));
                    sb.AppendLine("guard ssh=" + Core.Guard.CanDelete(profile + "\\.ssh\\id_rsa", true));
                    sb.AppendLine("guard cred=" + Core.Guard.CanDelete(profile + "\\AppData\\Roaming\\Microsoft\\Credentials\\x", true));
                    sb.AppendLine("guard whitelist-hit=" + Core.Guard.CanDelete(Environment.GetEnvironmentVariable("USERPROFILE") + "\\.vscode\\extensions\\x", true));
                    sb.AppendLine("guard temp=" + Core.Guard.CanDelete(temp, true));
                    sb.AppendLine("guard win-temp=" + Core.Guard.CanDelete("C:\\Windows\\Temp", true));
                    sb.AppendLine("guard win-system32=" + Core.Guard.CanDelete("C:\\Windows\\System32\\drivers", true));
                    sb.AppendLine("guard update-cache=" + Core.Guard.CanDelete("C:\\Windows\\SoftwareDistribution\\Download", true));
                    sb.AppendLine("guard winsxs=" + Core.Guard.CanDelete("C:\\Windows\\WinSxS", true));

                    string sandbox = System.IO.Path.Combine(temp, "agcleaner_selftest_" + DateTime.Now.Ticks);
                    System.IO.Directory.CreateDirectory(sandbox);
                    for (int i = 0; i < 3; i++)
                    {
                        byte[] data = new byte[1024 * 1024];
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(sandbox, "f" + i + ".bin"), data);
                    }
                    long before = Core.FileUtil.SizeOf(sandbox);
                    sb.AppendLine("sandbox size=" + before);

                    Core.CleanItem it = new Core.CleanItem();
                    it.Path = sandbox;
                    it.Kind = Core.CleanKind.DirItself;
                    it.Bytes = before;
                    it.Recycle = false;
                    it.Exists = true;
                    System.Collections.Generic.List<Core.CleanItem> items = new System.Collections.Generic.List<Core.CleanItem>();
                    items.Add(it);
                    int errors = 0;
                    System.Collections.Generic.List<string> log = null;
                    long freed = Core.CleanEngine.Execute(items, out log, out errors);
                    sb.AppendLine("permanent delete freed=" + freed + " errors=" + errors + " gone=" + (!System.IO.Directory.Exists(sandbox)));

                    System.IO.Directory.CreateDirectory(sandbox);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(sandbox, "g.bin"), new byte[512 * 1024]);
                    Core.CleanItem it2 = new Core.CleanItem();
                    it2.Path = sandbox;
                    it2.Kind = Core.CleanKind.DirItself;
                    it2.Bytes = 512 * 1024;
                    it2.Recycle = true;
                    it2.Exists = true;
                    items.Clear();
                    items.Add(it2);
                    freed = Core.CleanEngine.Execute(items, out log, out errors);
                    sb.AppendLine("recycle delete freed=" + freed + " errors=" + errors + " gone=" + (!System.IO.Directory.Exists(sandbox)));

                    // 白名单增删
                    Core.Guard.AddWhitelist("%USERPROFILE%\\selftest-wl");
                    bool hit = Core.Guard.IsWhitelisted(profile + "\\selftest-wl\\x");
                    Core.Guard.RemoveWhitelist("%USERPROFILE%\\selftest-wl");
                    sb.AppendLine("whitelist add/hit/remove=" + hit + "/" + (!Core.Guard.IsWhitelisted(profile + "\\selftest-wl\\x")));

                    Core.OpLog.Write("selftest", sandbox, 1024);
                    sb.AppendLine("oplog entries=" + Core.OpLog.Read(10).Count);
                    sb.AppendLine("admin=" + Core.Native.IsAdmin());
                    sb.AppendLine("RESULT=OK");
                }
                catch (Exception ex) { sb.AppendLine("RESULT=FAIL " + ex.ToString()); }
                System.IO.File.WriteAllText(outFile, sb.ToString(), System.Text.Encoding.UTF8);
                return;
            }

            if (args.Length >= 1 && args[0] == "--scan")
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                try
                {
                    string temp = System.Environment.GetEnvironmentVariable("TEMP");
                    sb.AppendLine("TEMP=" + (temp == null ? "<null>" : temp));
                    sb.AppendLine("LAD=" + Core.CleanEngine.LocalAppData);
                    sb.AppendLine("WL count=" + Core.Guard.WhiteList().Count);
                    sb.AppendLine("CAN(deletable)=" + Core.Guard.CanDelete(temp, true));
                    long tb = 0; int tf = 0;
                    Core.FileUtil.SizeOfOld(temp, System.DateTime.Now.AddDays(-7), ref tb, ref tf, 0);
                    sb.AppendLine("SIZEOLD=" + tb + " bytes / " + tf + " files");
                }
                catch (Exception ex) { sb.AppendLine("STEP1 EX " + ex.ToString()); }
                try
                {
                    Core.Settings st = Core.Settings.Load();
                    System.Collections.Generic.List<Core.CleanCategory> cats = Core.CleanEngine.Scan(st, (Action<string>)null);
                    for (int i = 0; i < cats.Count; i++)
                    {
                        sb.AppendLine(cats[i].Name + " -> " + cats[i].Items.Count + " items, " + Core.Byt.Format(cats[i].TotalBytes()));
                        for (int j = 0; j < cats[i].Items.Count && j < 8; j++)
                        {
                            sb.AppendLine("    " + cats[i].Items[j].Name + " | " + Core.Byt.Format(cats[i].Items[j].Bytes) + " | " + cats[i].Items[j].Path);
                        }
                    }
                    sb.AppendLine("TEMP=" + System.Environment.GetEnvironmentVariable("TEMP"));
                    sb.AppendLine("CAN=" + Core.Guard.CanDelete(System.Environment.GetEnvironmentVariable("TEMP"), true));
                }
                catch (Exception ex) { sb.AppendLine("EX " + ex.ToString()); }
                sb.AppendLine("--- 各步耗时 ---");
                for (int i = 0; i < Core.CleanEngine.Timings.Count; i++) sb.AppendLine(Core.CleanEngine.Timings[i]);
                System.IO.File.WriteAllText(args.Length >= 2 ? args[1] : "scan.txt", sb.ToString());
                return;
            }

            if (args.Length >= 1 && args[0] == "--orphans")
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                try
                {
                    System.Collections.Generic.List<Core.OrphanGroup> gs = Core.Orphans.Scan(null);
                    for (int i = 0; i < gs.Count; i++)
                    {
                        sb.AppendLine("[" + gs[i].Name + "] " + gs[i].Items.Count + Lang.T(" 项"));
                        for (int j = 0; j < gs[i].Items.Count && j < 40; j++)
                        {
                            sb.AppendLine("    " + gs[i].Items[j].Kind + " | " + Core.Byt.Format(gs[i].Items[j].Bytes) + " | " + gs[i].Items[j].Name + " | " + gs[i].Items[j].Target);
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine("EX " + ex.ToString()); }
                System.IO.File.WriteAllText(args.Length >= 2 ? args[1] : "orphans.txt", sb.ToString());
                return;
            }

            if (args.Length >= 2 && args[0] == "--guard")
            {
                // 诊断单条路径的判定结果，便于核对为什么某项被跳过
                System.Text.StringBuilder g = new System.Text.StringBuilder();
                for (int i = 1; i < args.Length; i++)
                {
                    string path = args[i];
                    g.AppendLine(path);
                    g.AppendLine("  can-delete = " + Core.Guard.CanDelete(path, true));
                    g.AppendLine("  protected  = " + Core.Guard.IsProtected(path));
                    g.AppendLine("  whitelist  = " + Core.Guard.IsWhitelisted(path));
                    g.AppendLine("  sensitive  = " + Core.Guard.IsSensitiveSegment(path));
                    g.AppendLine("  trusted    = " + Core.Guard.IsTrustedForClean(path));
                    g.AppendLine("  exists     = " + System.IO.Directory.Exists(path));
                }
                System.IO.File.WriteAllText("guard.txt", g.ToString(), System.Text.Encoding.UTF8);
                return;
            }

            if (args.Length >= 2 && args[0] == "--startup")
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                System.Collections.Generic.List<AGCleaner.Core.StartupItem> list = AGCleaner.Core.Startup.List();
                for (int i = 0; i < list.Count; i++)
                {
                    AGCleaner.Core.StartupItem it = list[i];
                    sb.Append(it.GroupText).Append(" | ").Append(it.Enabled ? "on " : "off").Append(" | ");
                    sb.Append(it.Name).Append(" | ").Append(it.Command).Append(" | ").Append(it.Source);
                    sb.Append(" | chars=");
                    for (int c = 0; c < it.Name.Length; c++) sb.Append(((int)it.Name[c]).ToString("X4")).Append(' ');
                    sb.AppendLine();
                }
                System.IO.File.WriteAllText(args[1], sb.ToString(), System.Text.Encoding.UTF8);
                return;
            }

            if (args.Length >= 3 && args[0] == "--startup-toggle")
            {
                // 诊断用：--startup-toggle <名称片段> off|on
                bool wantOff = args[2] == "off";
                System.Text.StringBuilder log = new System.Text.StringBuilder();
                System.Collections.Generic.List<AGCleaner.Core.StartupItem> list = AGCleaner.Core.Startup.List();
                for (int i = 0; i < list.Count; i++)
                {
                    AGCleaner.Core.StartupItem it = list[i];
                    if (args.Length < 4)
                    {
                        if (!it.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    else
                    {
                        if (it.Name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    }
                    if (it.Enabled == wantOff)
                    {
                        string msg;
                        bool ok = wantOff ? AGCleaner.Core.Startup.Disable(it, out msg) : AGCleaner.Core.Startup.Enable(it, out msg);
                        log.AppendLine((ok ? "OK   " : "FAIL ") + it.GroupText + " | " + it.Name + " | " + msg);
                        if (!ok) Environment.ExitCode = 1;
                    }
                }
                System.IO.File.WriteAllText("startup-toggle.txt", log.ToString(), System.Text.Encoding.UTF8);
                return;
            }

            if (args.Length >= 2 && args[0] == "--dlgshot")
            {
                // 离屏渲染“清理完成”报告框，用于自检排版
                ReportForm f = new ReportForm();
                f.Headline = Lang.T("清理完成");
                f.Summary = Lang.T("释放空间 1.73 GB · 处理 42 项 · 明细 42 条");
                f.Hint = Lang.T("全部处理完成；来源路径与体积已写入操作日志，可在历史记录页导出 CSV");
                f.ClientSize = new System.Drawing.Size(660, 500);
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(-4000, 0);
                f.ShowInTaskbar = false;
                AGCleaner.VList list = new AGCleaner.VList();
                list.ShowCheck = false;
                list.RightHeader = "";
                list.RowHeight = 28;
                list.SetBounds(20, 76, 620, 300);
                System.Collections.Generic.List<AGCleaner.VRow> rows = new System.Collections.Generic.List<AGCleaner.VRow>();
                string[] demo = new string[]
                {
                    Lang.T("用户临时目录  55.4 MB"), Lang.T("Windows 更新下载缓存  346 MB"), Lang.T("事件日志  172 MB"),
                    Lang.T("字体缓存  48.8 MB"), Lang.T("搜索索引数据库  454 MB"), Lang.T("安装包缓存 Package Cache  493 MB"),
                    "Chrome · Profile 5\\Cache  67.3 MB", "Chrome · Profile 5\\Code Cache  844 KB",
                    "Edge · Default\\Cache  22.6 MB", Lang.T("系统日志 Logs  13 MB"), Lang.T("交付优化缓存  6.27 MB")
                };
                for (int i = 0; i < demo.Length; i++)
                {
                    AGCleaner.VRow r = new AGCleaner.VRow();
                    r.Title = demo[i];
                    rows.Add(r);
                }
                list.SetRows(rows);
                f.Controls.Add(list);
                AGCleaner.Btn b1 = new AGCleaner.Btn();
                b1.Kind = AGCleaner.ButtonKind.Subtle;
                b1.Text = Lang.T("打开操作日志");
                b1.Size = new System.Drawing.Size(112, 34);
                b1.Location = new System.Drawing.Point(20, 448);
                f.Controls.Add(b1);
                AGCleaner.Btn b2 = new AGCleaner.Btn();
                b2.Kind = AGCleaner.ButtonKind.Accent;
                b2.Text = Lang.T("完成");
                b2.Size = new System.Drawing.Size(96, 34);
                b2.Location = new System.Drawing.Point(540, 448);
                f.Controls.Add(b2);
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
                return;
            }

            if (args.Length >= 2 && args[0] == "--langshot")
            {
                // 离屏渲染语言选择弹窗，检查关闭按钮与排版
                LangPicker f = new LangPicker();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(-4000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
                return;
            }

            if (args.Length >= 2 && args[0] == "--docshot")
            {
                // 离屏渲染内置文档窗口，用于自检嵌入资源是否读得到
                DocForm f = new DocForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(-4000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
                return;
            }

            if (args.Length >= 2 && args[0] == "--shot")
            {
                int page = 0;
                if (args.Length >= 3) int.TryParse(args[2], out page);
                string themeMode = "";
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "light" || args[i] == "dark" || args[i] == "system") themeMode = args[i];
                }
                if (themeMode.Length > 0) Theme.ApplyMode(themeMode, Theme.SystemLight());
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "en" || args[i] == "zh" || args[i] == "de" || args[i] == "fr" || args[i] == "ru" || args[i] == "es") Lang.Set(args[i]);
                }
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("filter:")) StartupPage.PendingFilter = args[i].Substring(7);
                }
                MainForm f = new MainForm();
                if (themeMode.Length > 0) f.SyncThemeVisualMode(themeMode);
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                f.SelectPage(page);
                Application.DoEvents();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "reload") { f.ReloadPages(page); Application.DoEvents(); }
                }
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "toggle") { Theme.ApplySkin(Theme.Skin.SystemLight); f.ApplyTheme(); }
                }
                Application.DoEvents();
                int waitMs = 600;
                if (args.Length >= 4) int.TryParse(args[3], out waitMs);
                int waited = 0;
                while (waited < waitMs)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                    waited += 50;
                }
                Application.DoEvents();
                f.ExportShot(args[1]);
                return;
            }

            MainForm form = new MainForm();
            Application.Run(form);
        }

        /// <summary>把未处理异常写入 crash.log，并弹出可读提示，避免直接出现系统错误框。</summary>
        private static void ReportCrash(Exception ex)
        {
            if (ex == null) return;
            string path = "crash.log";
            try
            {
                string dir = Core.Store.RootDir;
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir, "crash.log");
                string text = "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " lang=" + Lang.Cur + " ver=" + Version + "\r\n" +
                              ex.ToString() + "\r\n";
                System.IO.File.AppendAllText(path, text, System.Text.Encoding.UTF8);
            }
            catch (Exception) { }
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    Lang.T("程序遇到一个内部错误，已记录到日志：") + "\r\n" + path + "\r\n\r\n" + ex.Message,
                    "AG Cleaner", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
            catch (Exception) { }
        }
    }

    /// <summary>把滚轮事件转给鼠标下的可滚动控件。</summary>
    public class WheelRouter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            Core.Native.POINT pt = new Core.Native.POINT();
            pt.X = Cursor.Position.X;
            pt.Y = Cursor.Position.Y;
            IntPtr h = Core.Native.WindowFromPoint(pt);
            Control c = Control.FromHandle(h);
            if (c == null) return false;

            Control walk = c;
            int guard = 0;
            while (walk != null && guard++ < 20)
            {
                VList vl = walk as VList;
                if (vl != null)
                {
                    vl.ScrollBy(-(short)((long)m.WParam >> 16) / 120 * 30);
                    return true;
                }
                ScrollView sv = walk as ScrollView;
                if (sv != null)
                {
                    sv.ScrollBy(-(short)((long)m.WParam >> 16) / 120 * 60);
                    return true;
                }
                walk = walk.Parent;
            }
            return false;
        }
    }
}


