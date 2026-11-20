using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CCDesk
{
    public static class Program
    {
        public static string Version = "1.3.0";

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.AddMessageFilter(new WheelRouter());

            Core.Settings startSettings = Core.Settings.Load();
            Theme.Apply(startSettings.ThemeMode == "light");

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

                    string sandbox = System.IO.Path.Combine(temp, "ccdesk_selftest_" + DateTime.Now.Ticks);
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
                        sb.AppendLine("[" + gs[i].Name + "] " + gs[i].Items.Count + " 项");
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

            if (args.Length >= 2 && args[0] == "--dlgshot")
            {
                // 离屏渲染“清理完成”报告框，用于自检排版
                ReportForm f = new ReportForm();
                f.Headline = "清理完成";
                f.Summary = "释放空间 1.73 GB · 处理 42 项 · 明细 42 条";
                f.Hint = "全部处理完成；来源路径与体积已写入操作日志，可在历史记录页导出 CSV";
                f.ClientSize = new System.Drawing.Size(660, 500);
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(-4000, 0);
                f.ShowInTaskbar = false;
                CCDesk.VList list = new CCDesk.VList();
                list.ShowCheck = false;
                list.RightHeader = "";
                list.RowHeight = 28;
                list.SetBounds(20, 76, 620, 300);
                System.Collections.Generic.List<CCDesk.VRow> rows = new System.Collections.Generic.List<CCDesk.VRow>();
                string[] demo = new string[]
                {
                    "用户临时目录  55.4 MB", "Windows 更新下载缓存  346 MB", "事件日志  172 MB",
                    "字体缓存  48.8 MB", "搜索索引数据库  454 MB", "安装包缓存 Package Cache  493 MB",
                    "Chrome · Profile 5\\Cache  67.3 MB", "Chrome · Profile 5\\Code Cache  844 KB",
                    "Edge · Default\\Cache  22.6 MB", "系统日志 Logs  13 MB", "交付优化缓存  6.27 MB"
                };
                for (int i = 0; i < demo.Length; i++)
                {
                    CCDesk.VRow r = new CCDesk.VRow();
                    r.Title = demo[i];
                    rows.Add(r);
                }
                list.SetRows(rows);
                f.Controls.Add(list);
                CCDesk.Btn b1 = new CCDesk.Btn();
                b1.Kind = CCDesk.ButtonKind.Subtle;
                b1.Text = "打开操作日志";
                b1.Size = new System.Drawing.Size(112, 34);
                b1.Location = new System.Drawing.Point(20, 448);
                f.Controls.Add(b1);
                CCDesk.Btn b2 = new CCDesk.Btn();
                b2.Kind = CCDesk.ButtonKind.Accent;
                b2.Text = "完成";
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

            if (args.Length >= 2 && args[0] == "--shot")
            {
                int page = 0;
                if (args.Length >= 3) int.TryParse(args[2], out page);
                for (int i = 0; i < args.Length; i++) { if (args[i] == "light") Theme.Apply(true); if (args[i] == "dark") Theme.Apply(false); }
                MainForm f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                f.SelectPage(page);
                Application.DoEvents();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "toggle") { Theme.Apply(true); f.ApplyTheme(); }
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

