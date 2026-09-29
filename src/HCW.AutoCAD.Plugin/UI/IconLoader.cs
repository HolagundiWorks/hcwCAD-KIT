using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Loads the Carbon Design System PNG icons embedded as resources
    /// (Resources/Icons/16/*.png, Resources/Icons/32/*.png - IBM Carbon
    /// icon set, Apache-2.0) and hands back WPF ImageSources for ribbon
    /// buttons (RibbonButton.Image / LargeImage).
    /// </summary>
    public static class IconLoader
    {
        private static readonly Assembly Asm = typeof(IconLoader).Assembly;
        private const string ResourceRoot = "HCW.AutoCAD.Plugin.Resources.Icons";

        public static BitmapImage Load(string iconName, int size)
        {
            // Folder names "16"/"32" become "_16"/"_32" in the embedded resource
            // name because C# identifiers can't start with a digit.
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
                return bmp;
            }
        }

        public static BitmapImage Small(string iconName) => Load(iconName, 16);
        public static BitmapImage Large(string iconName) => Load(iconName, 32);
    }
}
