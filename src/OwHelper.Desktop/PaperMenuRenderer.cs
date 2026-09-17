using System.Drawing;
using System.Windows.Forms;

namespace OwHelper.Desktop;

public sealed class PaperMenuRenderer : ToolStripProfessionalRenderer
{
    public PaperMenuRenderer() : base(new PaperColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Palette.Ink : Palette.InkSecondary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Color color = e.Item.Selected ? Palette.AccentWash : Palette.Panel;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    sealed class PaperColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Palette.AccentWash;
        public override Color MenuItemSelectedGradientBegin => Palette.AccentWash;
        public override Color MenuItemSelectedGradientEnd => Palette.AccentWash;
        public override Color MenuItemBorder => Palette.Accent;
        public override Color MenuBorder => Palette.Border;
        public override Color ToolStripDropDownBackground => Palette.Panel;
        public override Color ImageMarginGradientBegin => Palette.Panel;
        public override Color ImageMarginGradientMiddle => Palette.Panel;
        public override Color ImageMarginGradientEnd => Palette.Panel;
        public override Color SeparatorDark => Palette.Border;
        public override Color SeparatorLight => Palette.Panel;
    }
}

