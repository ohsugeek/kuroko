using System.Drawing;
using System.Windows.Forms;

namespace Kuroko;

/// <summary>
/// タスクトレイのコンテキストメニュー(WinForms ContextMenuStrip)を Kuroko ブランドの
/// ダークテーマで描画する。定式幕配色(墨黒/柿)に合わせる。
/// </summary>
public sealed class KurokoMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Kinari = Color.FromArgb(0xF4, 0xF0, 0xE9);
    private static readonly Color TextMuted = Color.FromArgb(0x7C, 0x76, 0x6D);

    public KurokoMenuRenderer() : base(new KurokoColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Kinari : TextMuted;
        base.OnRenderItemText(e);
    }

    private sealed class KurokoColorTable : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(0x21, 0x1D, 0x18);
        private static readonly Color Hover = Color.FromArgb(0x32, 0x2C, 0x25);
        private static readonly Color Kaki = Color.FromArgb(0xD6, 0x5A, 0x2E);
        private static readonly Color BorderCol = Color.FromArgb(0x2A, 0x25, 0x1F);

        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;

        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => BorderCol;

        public override Color SeparatorDark => BorderCol;
        public override Color SeparatorLight => BorderCol;

        // チェック(設定トグル)は柿色で示す
        public override Color CheckBackground => Kaki;
        public override Color CheckSelectedBackground => Kaki;
        public override Color CheckPressedBackground => Kaki;
    }
}
