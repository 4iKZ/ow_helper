using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OwHelper.Desktop;

/// <summary>
/// 纯 GDI+ 双缓冲现代圆角按钮，支持微圆角、平滑抗锯齿与完美居中文本，杜绝黑色边缘
/// </summary>
public class RoundedButton : Button
{
    int cornerRadius = 7;
    Color borderColor = Palette.Border;
    int borderSize = 1;
    Color? hoverBackColor;
    Color? pressedBackColor;
    bool isHovered;
    bool isPressed;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.5f);
    }

    public int CornerRadius
    {
        get => cornerRadius;
        set { cornerRadius = Math.Max(0, value); Invalidate(); }
    }

    public Color BorderColor
    {
        get => borderColor;
        set { borderColor = value; Invalidate(); }
    }

    public int BorderSize
    {
        get => borderSize;
        set { borderSize = Math.Max(0, value); Invalidate(); }
    }

    public Color? HoverBackColor
    {
        get => hoverBackColor;
        set { hoverBackColor = value; Invalidate(); }
    }

    public Color? PressedBackColor
    {
        get => pressedBackColor;
        set { pressedBackColor = value; Invalidate(); }
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

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        isHovered = false;
        isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (Width <= 0 || Height <= 0) return;

        // 1. 彻底用父容器实际可见背景色清屏，杜绝未初始化黑色像素与倒角黑边
        Color parentBg = GetEffectiveParentBackColor();
        g.Clear(parentBg);

        // 计算当前背景色
        Color currentBg = BackColor;
        Color effectiveHover = hoverBackColor ?? (FlatAppearance.MouseOverBackColor != Color.Empty ? FlatAppearance.MouseOverBackColor : (Color?)null) ?? currentBg;
        Color effectivePressed = pressedBackColor ?? (FlatAppearance.MouseDownBackColor != Color.Empty ? FlatAppearance.MouseDownBackColor : (Color?)null) ?? effectiveHover;

        if (!Enabled)
        {
            currentBg = Palette.SurfaceSubtle;
        }
        else if (isPressed)
        {
            currentBg = effectivePressed;
        }
        else if (isHovered)
        {
            currentBg = effectiveHover;
        }

        // 2. 绘制圆角背景与边框（笔画居中对齐，内缩 strokeInset 防止溢出或留缝）
        float strokeInset = borderSize > 0 ? (borderSize / 2f) : 0f;
        var rectF = new RectangleF(
            strokeInset,
            strokeInset,
            Math.Max(1f, Width - borderSize),
            Math.Max(1f, Height - borderSize));

        using (var path = GetRoundedRectanglePath(rectF, cornerRadius))
        {
            // 填充圆角背景
            using (var brush = new SolidBrush(currentBg))
            {
                g.FillPath(brush, path);
            }

            // 绘制圆角边框
            if (borderSize > 0 && borderColor != Color.Transparent)
            {
                using var pen = new Pen(borderColor, borderSize);
                g.DrawPath(pen, path);
            }
        }

        // 3. 居中或按 TextAlign 绘制文本
        if (!string.IsNullOrEmpty(Text))
        {
            Color currentText = Enabled ? ForeColor : Palette.InkMuted;
            using var textBrush = new SolidBrush(currentText);
            (StringAlignment hAlign, StringAlignment vAlign) = AlignToStringFormat(TextAlign);
            using var sf = new StringFormat
            {
                Alignment = hAlign,
                LineAlignment = vAlign,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            var textRect = new Rectangle(
                Padding.Left,
                Padding.Top,
                Math.Max(1, Width - Padding.Horizontal),
                Math.Max(1, Height - Padding.Vertical));

            if (isPressed)
            {
                textRect.Offset(0, 1);
            }

            g.DrawString(Text, Font, textBrush, textRect, sf);
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

    static (StringAlignment, StringAlignment) AlignToStringFormat(ContentAlignment align) => align switch
    {
        ContentAlignment.TopLeft => (StringAlignment.Near, StringAlignment.Near),
        ContentAlignment.TopCenter => (StringAlignment.Center, StringAlignment.Near),
        ContentAlignment.TopRight => (StringAlignment.Far, StringAlignment.Near),
        ContentAlignment.MiddleLeft => (StringAlignment.Near, StringAlignment.Center),
        ContentAlignment.MiddleRight => (StringAlignment.Far, StringAlignment.Center),
        ContentAlignment.BottomLeft => (StringAlignment.Near, StringAlignment.Far),
        ContentAlignment.BottomCenter => (StringAlignment.Center, StringAlignment.Far),
        ContentAlignment.BottomRight => (StringAlignment.Far, StringAlignment.Far),
        _ => (StringAlignment.Center, StringAlignment.Center),
    };

    static GraphicsPath GetRoundedRectanglePath(RectangleF bounds, float radius)
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
