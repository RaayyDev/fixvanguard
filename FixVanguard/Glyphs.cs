using System.Drawing.Text;

namespace FixVanguard;

internal static class Glyphs
{
    public const string Play = "\uE768";
    public const string Delete = "\uE74D";
    public const string Shield = "\uEA18";
    public const string Chip = "\uE950";
    public const string Cloud = "\uE753";
    public const string Window = "\uE7C4";
    public const string Terminal = "\uE756";

    public static readonly Font Small;
    public static readonly Font Medium;

    static Glyphs()
    {
        var family = ResolveFamily();
        Small = new Font(family, 11f);
        Medium = new Font(family, 13f);
    }

    private static FontFamily ResolveFamily()
    {
        using var installed = new InstalledFontCollection();
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var match = installed.Families.FirstOrDefault(family =>
                family.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return FontFamily.GenericSansSerif;
    }
}
