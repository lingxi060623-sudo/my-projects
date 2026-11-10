// Renders Logo.Draw into a multi-size .ico (32-bit DIB frames, PNG for 256 px).
// Built and run by build.bat when src\app.ico is missing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AutoPaste
{
    static class MakeIcon
    {
        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

        static void Main(string[] args)
        {
            var frames = new List<byte[]>();
            foreach (int size in Sizes)
                frames.Add(Render(size));

            using (var file = new BinaryWriter(File.Create(args[0])))
            {
                file.Write((short)0);
                file.Write((short)1);
                file.Write((short)Sizes.Length);
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int size = Sizes[i];
                    file.Write((byte)(size >= 256 ? 0 : size));
                    file.Write((byte)(size >= 256 ? 0 : size));
                    file.Write((byte)0);
                    file.Write((byte)0);
                    file.Write((short)1);
                    file.Write((short)32);
                    file.Write(frames[i].Length);
                    file.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] frame in frames)
                    file.Write(frame);
            }
        }

        static byte[] Render(int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float margin = size >= 48 ? (float)Math.Round(size * 0.06) : size >= 24 ? 1 : 0;
                    Logo.Draw(g, new RectangleF(margin, margin, size - margin * 2, size - margin * 2));
                }
                using (var ms = new MemoryStream())
                {
                    if (size >= 256)
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                    WriteDib(bmp, ms);
                    return ms.ToArray();
                }
            }
        }

        // BITMAPINFOHEADER + bottom-up BGRA pixels + an empty AND mask (alpha carries transparency).
        static void WriteDib(Bitmap bmp, Stream stream)
        {
            int size = bmp.Width;
            var w = new BinaryWriter(stream);
            w.Write(40);
            w.Write(size);
            w.Write(size * 2);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    w.Write(c.B);
                    w.Write(c.G);
                    w.Write(c.R);
                    w.Write(c.A);
                }
            int maskStride = ((size + 31) / 32) * 4;
            w.Write(new byte[maskStride * size]);
            w.Flush();
        }
    }
}
