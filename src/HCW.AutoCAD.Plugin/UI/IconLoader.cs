using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Loads the Carbon Design System PNG icons embedded as resources
    /// and tints them for the current ribbon theme. AutoCAD recolours
    /// plain black icons, which makes them disappear on the light theme.
    /// A slight blue tint keeps the glyph dark on a light ribbon and light
    /// on a dark ribbon.
    /// </summary>
    public static class IconLoader
    {
        private static readonly Assembly Asm = typeof(IconLoader).Assembly;
        private const string ResourceRoot = "HCW.AutoCAD.Plugin.Resources.Icons";

        public static BitmapSource Small(string iconName) => Load(iconName, 16);
        public static BitmapSource Large(string iconName) => Load(iconName, 32);

        public static BitmapSource Load(string iconName, int size)
        {
            string resPath = $"{ResourceRoot}._{size}.{iconName}.png";
            using (var stream = Asm.GetManifestResourceStream(resPath))
            {
                if (stream == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return Tint(bmp);
            }
        }

        private static bool LightTheme()
        {
            try
            {
                object value = AcAp.GetSystemVariable("COLORTHEME");
                return Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static BitmapSource Tint(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[h * stride];
            converted.CopyPixels(pixels, stride, 0);
            bool light = LightTheme();
            byte red = light ? (byte)28 : (byte)248;
            byte green = light ? (byte)32 : (byte)248;
            byte blue = light ? (byte)48 : (byte)255;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 16)
                {
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                    continue;
                }
                pixels[i] = blue;
                pixels[i + 1] = green;
                pixels[i + 2] = red;
            }
            var tinted = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            tinted.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            tinted.Freeze();
            return tinted;
        }
    }
}
