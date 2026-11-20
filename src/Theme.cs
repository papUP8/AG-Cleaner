using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CCDesk
{
    /// <summary>统一配色、字体与绘制工具。深色/浅色两套主题可切换。</summary>
    public static class Theme
    {
        public static Color Bg;
        public static Color SidebarBg;
        public static Color TitleBg;
        public static Color TitleText;
        public static Color TitleDim;
        public static Color Card;
        public static Color CardHover;
        public static Color CardSoft;
        public static Color CardAlt;
        public static Color Border;
        public static Color BorderSoft;
        public static Color Text;
        public static Color TextDim;
        public static Color Muted;
        public static Color Hint;
        public static Color Accent;
        public static Color AccentDark;
        public static Color AccentSoft;
        public static Color AccentFg;
        public static Color Blue;
        public static Color BlueSoft;
        public static Color Warn;
        public static Color WarnSoft;
        public static Color Danger;
        public static Color DangerSoft;
        public static Color Track;
        public static Color Scroll;
        public static Color CheckBg;
        public static Color CheckMark;

        /// <summary>当前是否为浅色外观。</summary>
        public static bool IsLight { get; private set; }

        /// <summary>顶栏是否为蓝底（“系统”两档用白字，“浅色/深色”用深色字）。</summary>
        public static bool TitleOnBlue { get; private set; }

        public enum Skin { Light, Dark, SystemLight, SystemDark }

        static Theme()
        {
            Apply(false);
        }

        /// <summary>切换配色：深色（默认）或浅色。</summary>
        public static void Apply(bool light)
        {
            ApplySkin(light ? Skin.Light : Skin.Dark);
        }

        /// <summary>按三档模式配色：light / dark / system（system 再按 Windows 深浅色二选一）。</summary>
        public static void ApplyMode(string mode, bool systemLight)
        {
            if (mode == "light") ApplySkin(Skin.Light);
            else if (mode == "dark") ApplySkin(Skin.Dark);
            else ApplySkin(systemLight ? Skin.SystemLight : Skin.SystemDark);
        }

        /// <summary>四套皮肤：浅色（白顶栏）、深色（近黑顶栏）、系统浅色/系统深色（蓝顶栏）。</summary>
        public static void ApplySkin(Skin skin)
        {
            bool light = skin == Skin.Light || skin == Skin.SystemLight;
            IsLight = light;
            Base(light);
            if (skin == Skin.Light)
            {
                TitleBg = C("#F7F9FC"); TitleText = Text; TitleDim = Muted; TitleOnBlue = false;
            }
            else if (skin == Skin.Dark)
            {
                TitleBg = C("#0B1016"); TitleText = Text; TitleDim = Muted; TitleOnBlue = false;
            }
            else if (skin == Skin.SystemLight)
            {
                TitleBg = C("#1D5FC4"); TitleText = C("#FFFFFF"); TitleDim = C("#C7D9F2"); TitleOnBlue = true;
            }
            else
            {
                TitleBg = C("#0E2A4D"); TitleText = C("#EDF4FF"); TitleDim = C("#9CB6D8"); TitleOnBlue = true;
            }
        }

        private static void Base(bool light)
        {
            if (!light)
            {
                Bg = C("#0A0E13");
                SidebarBg = C("#0D131A");
                Card = C("#121A23");
                CardHover = C("#18222D");
                CardSoft = C("#0F1620");
                CardAlt = C("#0C1219");
                Border = C("#1F2A36");
                BorderSoft = C("#18222D");
                Text = C("#E6EDF3");
                TextDim = C("#A7B4C2");
                Muted = C("#7D8B9B");
                Hint = C("#5E6B7A");
                Accent = C("#2DD4A7");
                AccentDark = C("#19B98F");
                AccentSoft = C("#12332C");
                AccentFg = C("#04140F");
                Blue = C("#4C9AFF");
                BlueSoft = C("#142639");
                Warn = C("#E8B33B");
                WarnSoft = C("#332711");
                Danger = C("#F0605A");
                DangerSoft = C("#33191A");
                Track = C("#1B2632");
                Scroll = C("#2A3746");
                CheckBg = C("#1B2632");
                CheckMark = C("#06251D");
            }
            else
            {
                Bg = C("#ECEFF4");
                SidebarBg = C("#CBD7E4");
                Card = C("#FFFFFF");
                CardHover = C("#F4F7FB");
                CardSoft = C("#F1F4F9");
                CardAlt = C("#F8FAFC");
                Border = C("#C8D3E0");
                BorderSoft = C("#DDE5EE");
                Text = C("#16202B");
                TextDim = C("#41505F");
                Muted = C("#6A7788");
                Hint = C("#8A97A6");
                Accent = C("#0EA47A");
                AccentDark = C("#0B8A66");
                AccentSoft = C("#E4F6EF");
                AccentFg = C("#FFFFFF");
                Blue = C("#2B78C8");
                BlueSoft = C("#E8F1FB");
                Warn = C("#B57A08");
                WarnSoft = C("#FCF2DA");
                Danger = C("#D0453F");
                DangerSoft = C("#FBEAE8");
                Track = C("#E3E9F0");
                Scroll = C("#C6D0DA");
                CheckBg = C("#E9EEF4");
                CheckMark = C("#FFFFFF");
            }
        }

        /// <summary>读取 Windows 当前是否处于浅色外观（设置 → 个性化 → 颜色）。</summary>
        public static bool SystemLight()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v == null) return true;
                return Convert.ToInt32(v) == 1;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static Color C(string hex)
        {
            return ColorTranslator.FromHtml(hex);
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);
        }

        private static Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        public static Font F(float size)
        {
            return F(size, FontStyle.Regular);
        }

        public static Font F(float size, FontStyle style)
        {
            string key = size.ToString("0.0") + "|" + style.ToString();
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            Font created;
            try { created = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
            catch (Exception) { created = new Font("Segoe UI", size, style, GraphicsUnit.Point); }
            _fonts[key] = created;
            return created;
        }

        public static Font Mono(float size)
        {
            string key = "mono" + size.ToString("0.0");
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            Font created;
            try { created = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point); }
            catch (Exception) { created = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
            _fonts[key] = created;
            return created;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, p);
            }
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, p);
            }
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void Txt(Graphics g, string s, Font f, Color color, int x, int y)
        {
            using (SolidBrush b = new SolidBrush(color))
            {
                g.DrawString(s, f, b, x, y);
            }
        }

        public static void Txt(Graphics g, string s, Font f, Color color, Rectangle r, StringFormat fmt)
        {
            using (SolidBrush b = new SolidBrush(color))
            {
                g.DrawString(s, f, b, r, fmt);
            }
        }

        public static StringFormat SF(StringAlignment align, StringAlignment line)
        {
            StringFormat f = new StringFormat();
            f.Alignment = align;
            f.LineAlignment = line;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags = StringFormatFlags.NoWrap;
            return f;
        }

        public static StringFormat SF(StringAlignment align, StringAlignment line, bool wrap)
        {
            StringFormat f = SF(align, line);
            if (wrap) f.FormatFlags = 0;
            return f;
        }

        /// <summary>把字节转成人类可读的体积文本。</summary>
        public static string Size(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double b = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (b >= 1024 && u < units.Length - 1)
            {
                b /= 1024.0;
                u++;
            }
            if (u == 0) return ((long)b).ToString() + " B";
            if (b >= 100) return b.ToString("0") + " " + units[u];
            if (b >= 10) return b.ToString("0.0") + " " + units[u];
            return b.ToString("0.00") + " " + units[u];
        }

        public static string ShortPath(string path, int maxChars)
        {
            if (path == null) return "";
            if (path.Length <= maxChars) return path;
            int keep = Math.Max(6, maxChars - 3);
            string tail = path.Substring(path.Length - keep);
            int slash = tail.IndexOf('\\');
            if (slash > 0 && slash < tail.Length - 1) tail = tail.Substring(slash);
            return "..." + tail;
        }
    }
}

