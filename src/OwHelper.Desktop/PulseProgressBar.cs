using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OwHelper.Desktop;

/// <summary>
/// 纯 GDI+ 双缓冲平滑进度条，支持脉冲触发瞬间高亮反馈
/// </summary>
public sealed class PulseProgressBar : Control
{
    double progress;
    bool isFlashing;

    public PulseProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    public double Progress
    {
        get => progress;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(progress - clamped) > 0.001)
            {
                progress = clamped;
                Invalidate();
            }
        }
    }

    public bool IsFlashing
    {
        get => isFlashing;
        set
        {
            if (isFlashing != value)
            {
                isFlashing = value;
                Invalidate();
            }
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = proposedSize.Width > 0 ? proposedSize.Width : 240;
        int h = Height > 0 ? Height : 10;
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Height / 2;

        // 背景槽
        using (var bgBrush = new SolidBrush(Palette.AccentLight))
        {
            FillRoundedRect(g, bgBrush, bounds, radius);
        }

        // 填充进度
        int fillWidth = (int)(bounds.Width * progress);
        if (fillWidth > 2)
        {
            var fillBounds = new Rectangle(0, 0, fillWidth, bounds.Height);
            Color fillColor = isFlashing ? Color.Gold : Palette.Accent;
            using (var fillBrush = new SolidBrush(fillColor))
            {
                FillRoundedRect(g, fillBrush, fillBounds, radius);
            }
        }

        // 外边框
        using (var borderPen = new Pen(Palette.Border))
        {
            DrawRoundedRect(g, borderPen, bounds, radius);
        }
    }

    static void FillRoundedRect(Graphics g, Brush brush, Rectangle bounds, int radius)
    {
        using var path = GetRoundedPath(bounds, radius);
        g.FillPath(brush, path);
    }

    static void DrawRoundedRect(Graphics g, Pen pen, Rectangle bounds, int radius)
    {
        using var path = GetRoundedPath(bounds, radius);
        g.DrawPath(pen, path);
    }

    static GraphicsPath GetRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
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
}
