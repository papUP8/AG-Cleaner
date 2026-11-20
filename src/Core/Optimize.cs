using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CCDesk.Core
{
    public class OptimizeAction
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public string Glyph = "";
        public bool AdminOnly = false;
        public string Command = "";
        public bool LongRunning = false;
    }

    public static class Optimizer
    {
        public static List<OptimizeAction> Actions()
        {
            List<OptimizeAction> list = new List<OptimizeAction>();

            OptimizeAction dns = new OptimizeAction();
            dns.Id = "dns";
            dns.Name = Lang.T("刷新 DNS 解析缓存");
            dns.Desc = Lang.T("清理本机 DNS 缓存，修复域名解析失败或网页打开异常");
            dns.Glyph = "🌐";
            dns.Command = "ipconfig /flushdns";
            list.Add(dns);

            OptimizeAction arp = new OptimizeAction();
            arp.Id = "arp";
            arp.Name = Lang.T("清空 ARP 缓存");
            arp.Desc = Lang.T("重建局域网地址映射表，改善内网连接异常（需要管理员）");
            arp.Glyph = "🔗";
            arp.AdminOnly = true;
            arp.Command = "netsh interface ip delete arpcache";
            list.Add(arp);

            OptimizeAction trim = new OptimizeAction();
            trim.Id = "trim";
            trim.Name = Lang.T("优化系统盘（SSD TRIM）");
            trim.Desc = Lang.T("对系统盘执行 TRIM / 碎片整理，保持读写性能（可能需要数分钟）");
            trim.Glyph = "💽";
            trim.AdminOnly = true;
            trim.LongRunning = true;
            trim.Command = "defrag %SystemDrive% /O";
            list.Add(trim);

            OptimizeAction wu = new OptimizeAction();
            wu.Id = "wucache";
            wu.Name = Lang.T("清理 Windows 更新缓存");
            wu.Desc = Lang.T("清空 SoftwareDistribution 下载缓存，解决更新卡住与体积膨胀（需要管理员）");
            wu.Glyph = "🪟";
            wu.AdminOnly = true;
            wu.Command = "net stop wuauserv & rd /s /q %windir%\\SoftwareDistribution\\Download & net start wuauserv";
            list.Add(wu);

            OptimizeAction temp = new OptimizeAction();
            temp.Id = "temp";
            temp.Name = Lang.T("快速清理临时文件");
            temp.Desc = Lang.T("删除临时目录中 1 天前的文件，不进入深度扫描");
            temp.Glyph = "⚡";
            temp.Command = "";
            list.Add(temp);

            OptimizeAction rb = new OptimizeAction();
            rb.Id = "recycle";
            rb.Name = Lang.T("清空回收站");
            rb.Desc = Lang.T("释放回收站占用的磁盘空间（删除后不可恢复）");
            rb.Glyph = "🗑";
            rb.Command = "";
            list.Add(rb);

            return list;
        }

        public static bool Run(OptimizeAction a, Action<string> log, out string summary)
        {
            summary = Lang.T("完成");
            if (a.Id == "temp")
            {
                string temp = Environment.GetEnvironmentVariable("TEMP");
                int errors = 0;
                long freed = FileUtil.DeleteOldFiles(temp, DateTime.Now.AddDays(-1), true, ref errors);
                summary = Lang.T("已释放 ") + Byt.Format(freed) + (errors > 0 ? "，" + errors + Lang.T(" 项被占用跳过") : "");
                if (log != null) log(summary);
                OpLog.Write("optimize", Lang.T("快速清理临时文件"), freed);
                return true;
            }
            if (a.Id == "recycle")
            {
                long size = 0;
                int items = 0;
                Shell.RecycleBinInfo(ref size, ref items);
                bool ok = Shell.EmptyRecycleBin();
                summary = ok ? (Lang.T("已清空，释放 ") + Byt.Format(size)) : Lang.T("清空失败");
                if (log != null) log(summary);
                if (ok) OpLog.Write("emptied", "recycle:", size);
                return ok;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"" + a.Command + " 2>&1\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(a.LongRunning ? 600000 : 60000);
                if (log != null && output != null && output.Trim().Length > 0) log(output.Trim());
                summary = p.ExitCode == 0 ? Lang.T("执行完成") : (Lang.T("返回码 ") + p.ExitCode);
                OpLog.Write("optimize", a.Name, 0);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                summary = Lang.T("执行失败：") + ex.Message;
                if (log != null) log(summary);
                return false;
            }
        }
    }
}
