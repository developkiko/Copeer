using System.Drawing;

namespace Copeer.Core;

/// <summary>Палитра тёмной/светлой темы. Панели читают цвета при создании,
/// смена темы пересоздаёт окна (см. AppContext.ApplyTheme).</summary>
public static class Theme
{
    public static bool Light { get; private set; }

    public static void Set(string? name) => Light = name == "light";
    public static string Name => Light ? "light" : "dark";

    public static Color Back    => Light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(28, 28, 30);
    public static Color Panel   => Light ? Color.FromArgb(238, 238, 242) : Color.FromArgb(37, 37, 40);
    public static Color Field   => Light ? Color.White : Color.FromArgb(45, 45, 48);
    public static Color Button  => Light ? Color.FromArgb(228, 228, 233) : Color.FromArgb(55, 55, 60);
    public static Color Chip    => Light ? Color.FromArgb(228, 228, 233) : Color.FromArgb(45, 45, 48);
    public static Color Accent  => Light ? Color.FromArgb(43, 108, 224) : Color.FromArgb(64, 108, 220);
    public static Color SelRow  => Light ? Color.FromArgb(206, 224, 246) : Color.FromArgb(38, 79, 120);
    public static Color Text    => Light ? Color.FromArgb(28, 28, 32) : Color.White;
    public static Color TextDim => Light ? Color.FromArgb(105, 105, 112) : Color.FromArgb(150, 150, 158);
    public static Color Border  => Light ? Color.FromArgb(202, 202, 208) : Color.FromArgb(60, 60, 65);
    public static Color Ok      => Light ? Color.FromArgb(24, 128, 72) : Color.FromArgb(120, 220, 150);
    public static Color Err     => Light ? Color.FromArgb(196, 60, 54) : Color.FromArgb(240, 130, 120);
}
