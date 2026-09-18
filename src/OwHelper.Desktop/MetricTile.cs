using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OwHelper.Desktop;

/// <summary>
/// 现代 6px 圆角 KPI 指标卡片控件，双缓冲抗锯齿绘制居中标题与大号数值
/// </summary>
public class MetricTile : Control
{
    string title = "";
    string valueText = "--";
    Color valueColor = Palette.Ink;
    Font titleFont;
    Font valueFont;

    public MetricTile()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Palette.SurfaceSubtle;
        titleFont = new Font("Microsoft YaHei UI", 8.5f);
        valueFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
    }

    public string Title
    {
        get => title;
        set { title = value; Invalidate(); }
    }

    public string ValueText
    {
        get => valueText;
        set { valueText = value; Invalidate(); }
    }

    public Color ValueColor
    {
        get => valueColor;
        set { valueColor = value; Invalidate(); }
    }

    public float ValueFontSize
    {
        get => valueFont.Size;
        set
        {
            valueFont.Dispose();
            valueFont = new Font("Microsoft YaHei UI", value, FontStyle.Bold);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // 1. 绘制 6px 圆角背景和边框
        using (var path = GetRoundedRectanglePath(rect, 6))
        {
            using (var bgBrush = new SolidBrush(BackColor))
            {
                g.FillPath(bgBrush, path);
            }
            using (var borderPen = new Pen(Palette.Border, 1))
            {
                g.DrawPath(borderPen, path);
            }
        }

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        // 2. 绘制标题（居中偏上）
        var titleRect = new Rectangle(4, 5, Width - 8, 18);
        using (var titleBrush = new SolidBrush(Palette.InkMuted))
        {
            g.DrawString(title, titleFont, titleBrush, titleRect, sf);
        }

        // 3. 绘制主数值（居中偏下，自动适应卡片宽度避免截断）
        var valueRect = new Rectangle(4, 23, Width - 8, Height - 27);
        Font activeValueFont = valueFont;
        bool fontCreated = false;
        var measured = g.MeasureString(valueText, activeValueFont);
        if (measured.Width > valueRect.Width && activeValueFont.Size > 7.5f)
        {
            float targetSize = Math.Max(7.5f, (float)Math.Floor(activeValueFont.Size * (valueRect.Width / measured.Width) * 0.95f));
            activeValueFont = new Font(valueFont.FontFamily, targetSize, valueFont.Style);
            fontCreated = true;
        }

        using (var valueBrush = new SolidBrush(valueColor))
        {
            g.DrawString(valueText, activeValueFont, valueBrush, valueRect, sf);
        }

        if (fontCreated) activeValueFont.Dispose();
    }

    static GraphicsPath GetRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
