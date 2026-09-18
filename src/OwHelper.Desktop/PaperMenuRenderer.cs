using System.Drawing;
using System.Windows.Forms;

namespace OwHelper.Desktop;

public sealed class PaperMenuRenderer : ToolStripProfessionalRenderer
{
    public PaperMenuRenderer() : base(new ModernColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Palette.Ink : Palette.InkMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var brush = new SolidBrush(Palette.AccentLight);
            using var pen = new Pen(Palette.AccentWash);
            e.Graphics.FillRectangle(brush, rect);
            e.Graphics.DrawRectangle(pen, rect);
        }
        else
        {
            using var brush = new SolidBrush(Palette.Panel);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    sealed class ModernColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Palette.AccentLight;
        public override Color MenuItemSelectedGradientBegin => Palette.AccentLight;
        public override Color MenuItemSelectedGradientEnd => Palette.AccentLight;
        public override Color MenuItemBorder => Palette.AccentWash;
        public override Color MenuBorder => Palette.Border;
        public override Color ToolStripDropDownBackground => Palette.Panel;
        public override Color ImageMarginGradientBegin => Palette.Panel;
        public override Color ImageMarginGradientMiddle => Palette.Panel;
        public override Color ImageMarginGradientEnd => Palette.Panel;
        public override Color SeparatorDark => Palette.Border;
        public override Color SeparatorLight => Palette.Panel;
    }
}

