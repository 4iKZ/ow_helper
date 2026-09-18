using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OwHelper.Desktop;

/// <summary>
/// 纯 GDI+ 双缓冲现代圆角按钮，支持微圆角、平滑抗锯齿与完美居中文本
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
            ControlStyles.ResizeRedraw,
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

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0) return;

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

        using (var path = GetRoundedRectanglePath(rect, cornerRadius))
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

        // 居中或按 TextAlign 绘制文本
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

            // 计算文本矩形（扣除 Padding）
            var textRect = new Rectangle(
                rect.Left + Padding.Left,
                rect.Top + Padding.Top,
                Math.Max(1, rect.Width - Padding.Horizontal),
                Math.Max(1, rect.Height - Padding.Vertical));

            // 按压时微下沉 1 像素反馈
            if (isPressed)
            {
                textRect.Offset(0, 1);
            }

            g.DrawString(Text, Font, textBrush, textRect, sf);
        }
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

    static GraphicsPath GetRoundedRectanglePath(Rectangle bounds, int radius)
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
