using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;

namespace OwHelper.Tray;

public static class TrayIconFactory
{
    public static Icon Create(Color statusColor, int size = 16)
    {
        using Bitmap bitmap = Draw(statusColor, size);
        using var stream = new MemoryStream();
        WriteIco(stream, bitmap, size);
        stream.Position = 0;
        return new Icon(stream);
    }

    static Bitmap Draw(Color statusColor, int size)
    {
        var bitmap = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float s = size / 16f;
        PointF[] points =
        {
            new PointF(1f * s, 9f * s),
            new PointF(4f * s, 9f * s),
            new PointF(5.5f * s, 3.5f * s),
            new PointF(7.5f * s, 12.5f * s),
            new PointF(9f * s, 9f * s),
            new PointF(15f * s, 9f * s),
        };

        using var outline = new Pen(Palette.Paper, 3f * s)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var core = new Pen(statusColor, 1.6f * s)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLines(outline, points);
        g.DrawLines(core, points);
        return bitmap;
    }

    static void WriteIco(Stream stream, Bitmap bitmap, int size)
    {
        int xorSize = size * size * 4;
        int maskRowBytes = ((size + 31) / 32) * 4;
        int maskSize = maskRowBytes * size;

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(40 + xorSize + maskSize);
        writer.Write(22);

        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(xorSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                writer.Write(c.B);
                writer.Write(c.G);
                writer.Write(c.R);
                writer.Write(c.A);
            }
        }

        writer.Write(new byte[maskSize]);
        writer.Flush();
    }
}
