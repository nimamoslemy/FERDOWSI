using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrammerV
{
    public static class AppIconGenerator
    {
        private static ImageSource _cachedFlagSource;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource GetBrandFlagImageSource()
        {
            if (_cachedFlagSource != null)
            {
                return _cachedFlagSource;
            }

            try
            {
                byte[] rawBytes = null;
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream resStream = asm.GetManifestResourceStream("ferdowsi_flag.png"))
                {
                    if (resStream != null)
                    {
                        using (MemoryStream copyMs = new MemoryStream())
                        {
                            resStream.CopyTo(copyMs);
                            rawBytes = copyMs.ToArray();
                        }
                    }
                }

                if (rawBytes == null || rawBytes.Length == 0)
                {
                    string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ferdowsi_flag.png");
                    if (File.Exists(diskPath))
                    {
                        rawBytes = File.ReadAllBytes(diskPath);
                    }
                }

                if (rawBytes != null && rawBytes.Length > 0)
                {
                    using (MemoryStream ms = new MemoryStream(rawBytes))
                    {
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        _cachedFlagSource = bi;
                        return _cachedFlagSource;
                    }
                }
            }
            catch { }

            return null;
        }

        public static System.Windows.Controls.Image CreateBrandFlagImage(double height, Thickness margin)
        {
            System.Windows.Controls.Image img = new System.Windows.Controls.Image();
            ImageSource src = GetBrandFlagImageSource();
            if (src != null)
            {
                img.Source = src;
            }
            img.Height = height;
            img.Stretch = Stretch.Uniform;
            img.Margin = margin;
            img.VerticalAlignment = VerticalAlignment.Center;
            img.SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public static Icon CreateTrayIcon(int size)
        {
            using (Bitmap bmp = RenderLogoBitmap(size))
            {
                IntPtr hIcon = bmp.GetHicon();
                Icon temp = Icon.FromHandle(hIcon);
                Icon cloned = (Icon)temp.Clone();
                DestroyIcon(hIcon);
                return cloned;
            }
        }

        public static ImageSource CreateWpfIcon(int size)
        {
            using (Bitmap bmp = RenderLogoBitmap(size))
            {
                IntPtr hBmp = bmp.GetHbitmap();
                try
                {
                    BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                        hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DeleteObject(hBmp);
                }
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static Bitmap RenderLogoBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(System.Drawing.Color.Transparent);

                int pad = Math.Max(1, size / 16);
                Rectangle rect = new Rectangle(pad, pad, size - pad * 2, size - pad * 2);

                using (GraphicsPath path = RoundedRect(rect, size / 4))
                using (System.Drawing.Drawing2D.LinearGradientBrush bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                    rect,
                    System.Drawing.Color.FromArgb(255, 16, 185, 129),
                    System.Drawing.Color.FromArgb(255, 5, 150, 105),
                    45f))
                {
                    g.FillPath(bg, path);
                }

                float fontSize = size * 0.52f;
                using (Font f = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush white = new SolidBrush(System.Drawing.Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("F", f, white, new RectangleF(0, -size * 0.02f, size, size), sf);
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static string GetHelpPdfPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "FERDOWSI_Help_Guide.pdf");
            if (File.Exists(p1)) return p1;

            try
            {
                DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
                if (parent != null)
                {
                    string p2 = Path.Combine(parent.FullName, "FERDOWSI_Help_Guide.pdf");
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { }

            return p1;
        }

        public static void OpenHelpPdf()
        {
            try
            {
                string pdfPath = GetHelpPdfPath();
                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 1024)
                {
                    EnsureHelpPdfFile(pdfPath);
                }
                if (File.Exists(pdfPath))
                {
                    Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
                }
            }
            catch { }
        }

        public static void EnsureHelpPdfFile(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath)) return;
            try
            {
                string dir = Path.GetDirectoryName(pdfPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                byte[] page1Jpg = RenderHelpPageJpeg(false);
                byte[] page2Jpg = RenderHelpPageJpeg(true);
                WriteTwoPageJpegPdf(pdfPath, page1Jpg, page2Jpg, 1240, 1754);
            }
            catch { }
        }

        private static System.Windows.Media.SolidColorBrush WpfBrush(byte r, byte g, byte b)
        {
            System.Windows.Media.SolidColorBrush br = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static Border CreatePdfSectionCard(string title, string[] bullets, bool rtl, System.Windows.Media.Brush accentBrush)
        {
            Border card = new Border();
            card.Background = WpfBrush(248, 250, 252);
            card.BorderBrush = WpfBrush(203, 213, 225);
            card.BorderThickness = new Thickness(1.5);
            card.CornerRadius = new CornerRadius(12);
            card.Padding = new Thickness(24, 16, 24, 16);
            card.Margin = new Thickness(0, 0, 0, 16);
            card.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            StackPanel sp = new StackPanel();

            TextBlock hdr = new TextBlock();
            hdr.Text = title;
            hdr.FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Tahoma");
            hdr.FontSize = 21;
            hdr.FontWeight = System.Windows.FontWeights.Bold;
            hdr.Foreground = accentBrush;
            hdr.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(hdr);

            for (int i = 0; i < bullets.Length; i++)
            {
                TextBlock tb = new TextBlock();
                tb.Text = bullets[i];
                tb.FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Tahoma");
                tb.FontSize = 16.5;
                tb.Foreground = WpfBrush(30, 41, 59);
                tb.TextWrapping = TextWrapping.Wrap;
                tb.LineHeight = 25;
                tb.Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(tb);
            }

            card.Child = sp;
            return card;
        }

        private static byte[] RenderHelpPageJpeg(bool isPersianPage)
        {
            int width = 1240;
            int height = 1754;

            Border root = new Border();
            root.Width = width;
            root.Height = height;
            root.Background = WpfBrush(255, 255, 255);
            root.Padding = new Thickness(56, 48, 56, 44);
            root.FlowDirection = isPersianPage ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            StackPanel main = new StackPanel();

            // Header Banner
            Border banner = new Border();
            banner.Background = WpfBrush(15, 23, 42);
            banner.BorderBrush = WpfBrush(16, 185, 129);
            banner.BorderThickness = new Thickness(2);
            banner.CornerRadius = new CornerRadius(14);
            banner.Padding = new Thickness(28, 22, 28, 22);
            banner.Margin = new Thickness(0, 0, 0, 22);

            StackPanel bannerStack = new StackPanel();
            TextBlock titleTb = new TextBlock();
            titleTb.FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Tahoma");
            titleTb.FontSize = 30;
            titleTb.FontWeight = System.Windows.FontWeights.Bold;
            titleTb.Foreground = WpfBrush(52, 211, 153);
            titleTb.Text = isPersianPage
                ? "راهنمای کامل فردوسی نسخه ۲ (FERDOWSI v2)"
                : "FERDOWSI v2 — User Guide & API Setup Manual";
            bannerStack.Children.Add(titleTb);

            TextBlock subTb = new TextBlock();
            subTb.FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Tahoma");
            subTb.FontSize = 16.5;
            subTb.Foreground = WpfBrush(226, 232, 240);
            subTb.Margin = new Thickness(0, 6, 0, 0);
            subTb.Text = isPersianPage
                ? "صفحه ۲ از ۲  |  میانبرهای ویندوز، بازنویسی سفارشی، تایپ صوتی، بلندخوانی و دریافت کلیدهای API  |  https://github.com/nimamoslemy"
                : "Page 1 of 2 (English Guide — Persian Guide on Page 2)  |  https://github.com/nimamoslemy";
            bannerStack.Children.Add(subTb);
            banner.Child = bannerStack;
            main.Children.Add(banner);

            System.Windows.Media.Brush emerald = WpfBrush(5, 150, 105);
            System.Windows.Media.Brush blue = WpfBrush(37, 99, 235);
            System.Windows.Media.Brush purple = WpfBrush(124, 58, 237);
            System.Windows.Media.Brush amber = WpfBrush(180, 83, 9);

            if (!isPersianPage)
            {
                main.Children.Add(CreatePdfSectionCard(
                    "1. Global Windows Keyboard Shortcuts (Work in Any App)",
                    new string[]
                    {
                        "• Alt + G (or Ctrl + Shift + G)  —  Summon or toggle the expanded FERDOWSI card right above your cursor.",
                        "• Alt + V (or Ctrl + Shift + V)  —  Start / Stop Voice Dictation (🎤) anywhere in Windows.",
                        "• Alt + R (or Ctrl + Shift + R)  —  Read Aloud (🔊) / Stop Reading selected or current text.",
                        "• Alt + Q (or Ctrl + Shift + F)  —  Instant Quick-Fix (Fix ✨) of the current sentence at your cursor.",
                        "• Esc  —  Close the expanded card or stop active voice recording / reading aloud."
                    },
                    false,
                    emerald));

                main.Children.Add(CreatePdfSectionCard(
                    "2. Floating Short Window & Custom Reword / Format Box",
                    new string[]
                    {
                        "• Short Window Icon Order (Left to Right):  [🎤 Mic]   [🔊 Read Aloud]   [🔴 Error Count / ✓]   [Fix ✨]   [× Close / Pause 10m, 30m, 1h].",
                        "• Custom Reword & Format Box (✨ Reword Tab): Click inside the custom instruction box in the Reword tab, type any instruction or format you want (e.g. 'Formal email format', 'Translate to polite Persian', 'Summarize in 3 bullet points'), and click Apply ✨ or press Enter.",
                        "• Built-in Tone & Format Presets: Fluent, Formal, Casual, Concise, Expand, Persuasive, Finglish → فارسی, Persian نیم‌فاصله Fix, Bullet List, Numbered List, Email Format, and Bilingual Dictionary."
                    },
                    false,
                    blue));

                main.Children.Add(CreatePdfSectionCard(
                    "3. How to Get Cloud AI, Voice Dictation & Read Aloud API Keys",
                    new string[]
                    {
                        "• Google Gemini API (Recommended — Free Tier): Visit https://aistudio.google.com/apikey → Click 'Create API Key' → Paste into Google Gemini API Key in Settings → Click Save & Test. Powers Grammar, Custom Reword, Format, Dictionary, Voice Cleanup & Gemini TTS.",
                        "• DeepSeek API (Cloud AI Alternative): Visit https://platform.deepseek.com/api_keys → Create an API key → Paste into DeepSeek API Key in Settings.",
                        "• Groq Whisper API (Fastest Free-Tier Voice Dictation): Visit https://console.groq.com/keys → Create API Key (gsk_...) → Paste into Groq API Key in Settings.",
                        "• Deepgram Nova-2 & Aura API (Voice Dictation & Read Aloud): Visit https://console.deepgram.com → Create API Key → Paste into Deepgram API Key in Settings.",
                        "• Voice.ai API & Offline SAPI (Read Aloud): Add a Voice.ai key from https://voice.ai or select 'Windows Offline SAPI' for 100% free offline reading aloud."
                    },
                    false,
                    purple));

                main.Children.Add(CreatePdfSectionCard(
                    "4. Voice Modes, Voice Languages & Privacy Protection",
                    new string[]
                    {
                        "• Voice Output Mode (in Main Settings): Choose '✨ Cleanup Voice' (cleans up grammar, punctuation, and spoken self-corrections without ever answering back as a chatbot) or '🎙 Exact Voice' (types exact verbatim words spoken).",
                        "• Voice Languages (in Main Settings): Strictly restricted to English + Persian Mixed (EN+FA), Persian Only (فارسی), or English Only (EN).",
                        "• Password & Key Security: FERDOWSI automatically disables itself inside password fields. Your API keys are saved strictly in %APPDATA%\\FERDOWSI\\settings.json and never leave your PC."
                    },
                    false,
                    amber));
            }
            else
            {
                main.Children.Add(CreatePdfSectionCard(
                    "۱. میانبرهای سراسری ویندوز (قابل استفاده در تمامی برنامه‌ها)",
                    new string[]
                    {
                        "• Alt + G (یا Ctrl + Shift + G) :  باز و بسته کردن کارت کامل فردوسی در بالای مکان‌نما.",
                        "• Alt + V (یا Ctrl + Shift + V) :  شروع و توقف تایپ صوتی هوشمند (🎤) در هر کادر متنی ویندوز.",
                        "• Alt + R (یا Ctrl + Shift + R) :  بلندخوانی متن انتخاب‌شده یا جمله فعلی (🔊) و توقف پخش.",
                        "• Alt + Q (یا Ctrl + Shift + F) :  اصلاح فوری املا، نگارش و نیم‌فاصله جمله فعلی (Fix ✨).",
                        "• کلید Esc :  بستن پنجره بازشده یا توقف ضبط صدا / بلندخوانی."
                    },
                    true,
                    emerald));

                main.Children.Add(CreatePdfSectionCard(
                    "۲. ترتیب جدید آیکون‌ها در پنجره شناور و کادر دستور سفارشی (Reword)",
                    new string[]
                    {
                        "• ترتیب آیکون‌ها در پنجره کوچک (از چپ به راست): میکروفون (🎤) ، بلندگو (🔊) ، شماره تعداد خطا با آیکون قرمز (یا تیک سبز ✓) ، دکمه اصلاح فوری (Fix ✨) و دکمه بستن / توقف موقت ۱۰، ۳۰ یا ۶۰ دقیقه‌ای (×).",
                        "• کادر دستور سفارشی در تب ✨ Reword: در کادر متنی بالای تب Reword هر فرمت یا دستوری که می‌خواهید بنویسید (مثلاً: «به فرمت ایمیل رسمی بنویس»، «به فارسی اداری ترجمه کن»، «در ۳ مورد خلاصه کن») و دکمه Apply ✨ یا کلید Enter را بزنید.",
                        "• تغییر زبان تنظیمات: از بالای پنجره تنظیمات می‌توانید زبان محیط برنامه را بین English و فارسی تغییر دهید."
                    },
                    true,
                    blue));

                main.Children.Add(CreatePdfSectionCard(
                    "۳. راهنمای گام‌به‌گام دریافت کلیدهای API (هوش مصنوعی و صدا)",
                    new string[]
                    {
                        "• کلید Google Gemini API (پیشنهادی و دارای سهمیه رایگان): وارد سایت https://aistudio.google.com/apikey شوید، روی Create API Key کلیک کنید و کلید را در بخش Google Gemini در تنظیمات وارد نمایید.",
                        "• کلید DeepSeek API: از آدرس https://platform.deepseek.com/api_keys کلید بسازید و در بخش DeepSeek قرار دهید.",
                        "• کلید Groq Whisper API (سریع‌ترین تایپ صوتی رایگان فارسی و انگلیسی): وارد https://console.groq.com/keys شوید، کلید بسازید و در بخش Groq وارد کنید.",
                        "• کلید Deepgram API (تایپ صوتی و بلندخوانی): از سایت https://console.deepgram.com کلید دریافت کرده و در تنظیمات قرار دهید."
                    },
                    true,
                    purple));

                main.Children.Add(CreatePdfSectionCard(
                    "۴. تنظیمات زبان و حالت تایپ صوتی و امنیت رمزهای عبور",
                    new string[]
                    {
                        "• حالت خروجی تایپ صوتی (در صفحه اصلی تنظیمات): حالت ✨ Cleanup Voice تپق‌ها و غلط‌های نگارشی گفتار را اصلاح می‌کند (بدون اینکه به صحبت شما پاسخ اضافه بدهد)؛ حالت 🎙 Exact Voice دقیقاً کلمات گفته‌شده را تایپ می‌کند.",
                        "• زبان تایپ صوتی (در صفحه اصلی تنظیمات): محدود به سه حالت «انگلیسی + فارسی ترکیبی»، «فقط فارسی» و «فقط انگلیسی».",
                        "• امنیت کامل رمز عبور: هنگام تایپ در کادرهای رمز عبور (Password)، برنامه به‌طور خودکار غیرفعال می‌شود."
                    },
                    true,
                    amber));
            }

            root.Child = main;
            root.Measure(new System.Windows.Size(width, height));
            root.Arrange(new System.Windows.Rect(0, 0, width, height));
            root.UpdateLayout();

            RenderTargetBitmap rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);

            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = 92;
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        private static void WriteTwoPageJpegPdf(string pdfPath, byte[] jpg1, byte[] jpg2, int pxW, int pxH)
        {
            using (FileStream fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write))
            {
                List<long> offsets = new List<long>();
                offsets.Add(0); // obj 0 placeholder

                Action<string> writeAscii = delegate(string s)
                {
                    byte[] b = Encoding.ASCII.GetBytes(s);
                    fs.Write(b, 0, b.Length);
                };

                writeAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                // 1: Catalog
                offsets.Add(fs.Position);
                writeAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                // 2: Pages
                offsets.Add(fs.Position);
                writeAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>\nendobj\n");

                // 3: Page 1
                offsets.Add(fs.Position);
                writeAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

                // 4: Image 1
                offsets.Add(fs.Position);
                writeAscii(string.Format("4 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", pxW, pxH, jpg1.Length));
                fs.Write(jpg1, 0, jpg1.Length);
                writeAscii("\nendstream\nendobj\n");

                // 5: Content 1
                string content1 = "q\n595 0 0 842 0 0 cm\n/Im1 Do\nQ\n";
                offsets.Add(fs.Position);
                writeAscii(string.Format("5 0 obj\n<< /Length {0} >>\nstream\n{1}endstream\nendobj\n", content1.Length, content1));

                // 6: Page 2
                offsets.Add(fs.Position);
                writeAscii("6 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im2 7 0 R >> >> /Contents 8 0 R >>\nendobj\n");

                // 7: Image 2
                offsets.Add(fs.Position);
                writeAscii(string.Format("7 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", pxW, pxH, jpg2.Length));
                fs.Write(jpg2, 0, jpg2.Length);
                writeAscii("\nendstream\nendobj\n");

                // 8: Content 2
                string content2 = "q\n595 0 0 842 0 0 cm\n/Im2 Do\nQ\n";
                offsets.Add(fs.Position);
                writeAscii(string.Format("8 0 obj\n<< /Length {0} >>\nstream\n{1}endstream\nendobj\n", content2.Length, content2));

                long xrefPos = fs.Position;
                writeAscii(string.Format("xref\n0 {0}\n0000000000 65535 f \n", offsets.Count));
                for (int i = 1; i < offsets.Count; i++)
                {
                    writeAscii(string.Format("{0:D10} 00000 n \n", offsets[i]));
                }
                writeAscii(string.Format("trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count, xrefPos));
            }
        }
    }
}
