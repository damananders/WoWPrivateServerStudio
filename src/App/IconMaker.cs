using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AndersCraftStudio
{
    /// <summary>Renders the crest into a multi-size .ico (PNG-compressed entries). Used once at build time: --make-icon path.ico</summary>
    static class IconMaker
    {
        public static void Write(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (var s in sizes)
            {
                var v = U.Crest(s);
                v.Measure(new Size(s, s)); v.Arrange(new Rect(0, 0, s, s)); v.UpdateLayout();
                var rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream(); enc.Save(ms); pngs.Add(ms.ToArray());
            }
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset); offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }
}
