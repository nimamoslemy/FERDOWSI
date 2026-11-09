using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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
    }
}
