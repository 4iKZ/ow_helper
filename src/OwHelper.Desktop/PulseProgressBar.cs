using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OwHelper.Desktop;

/// <summary>
/// 纯 GDI+ 双缓冲平滑进度条，支持脉冲触发瞬间高亮反馈，杜绝边缘黑边
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
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
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

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        Invalidate();
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (Width <= 0 || Height <= 0) return;

        // 1. 彻底用父容器实际可见背景色清屏，杜绝未初始化黑色像素与倒角黑边
        Color parentBg = GetEffectiveParentBackColor();
        g.Clear(parentBg);

        float strokeInset = 0.5f;
        var bounds = new RectangleF(strokeInset, strokeInset, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
        float radius = bounds.Height / 2f;

        // 2. 绘制背景槽
        using (var bgBrush = new SolidBrush(Palette.AccentLight))
        {
            FillRoundedRect(g, bgBrush, bounds, radius);
        }

        // 3. 填充进度
        float fillWidth = (float)(bounds.Width * progress);
        if (fillWidth > 2f)
        {
            var fillBounds = new RectangleF(bounds.X, bounds.Y, fillWidth, bounds.Height);
            Color fillColor = isFlashing ? Color.Gold : Palette.Accent;
            using (var fillBrush = new SolidBrush(fillColor))
            {
                FillRoundedRect(g, fillBrush, fillBounds, radius);
            }
        }

        // 4. 外边框
        using (var borderPen = new Pen(Palette.Border, 1))
        {
            DrawRoundedRect(g, borderPen, bounds, radius);
        }
    }

    Color GetEffectiveParentBackColor()
    {
        Control? p = Parent;
        while (p != null)
        {
            if (p.BackColor != Color.Transparent && p.BackColor != Color.Empty && p.BackColor.A == 255)
            {
                return p.BackColor;
            }
            p = p.Parent;
        }
        return Palette.Paper;
    }

    static void FillRoundedRect(Graphics g, Brush brush, RectangleF bounds, float radius)
    {
        using var path = GetRoundedPath(bounds, radius);
        g.FillPath(brush, path);
    }

    static void DrawRoundedRect(Graphics g, Pen pen, RectangleF bounds, float radius)
    {
        using var path = GetRoundedPath(bounds, radius);
        g.DrawPath(pen, path);
    }

    static GraphicsPath GetRoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));
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
