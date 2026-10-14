using System.Reflection;
using System.Text;

namespace Witcher3FontTool;

[Flags]
public enum FontCoverage
{
    SimplifiedChinese = 1,
    TraditionalChinese = 2,
    English = 4,
    Russian = 8,
    Ukrainian = 16
}

public static class FontCharacterSets
{
    public const FontCoverage All = FontCoverage.SimplifiedChinese | FontCoverage.TraditionalChinese | FontCoverage.English | FontCoverage.Russian | FontCoverage.Ukrainian;
    public static readonly (FontCoverage Selection, string Key)[] Languages = {
        (FontCoverage.SimplifiedChinese, "cn"), (FontCoverage.TraditionalChinese, "zh"),
        (FontCoverage.English, "en"), (FontCoverage.Russian, "ru"), (FontCoverage.Ukrainian, "ua")
    };
    static readonly Lazy<IReadOnlySet<uint>> Simplified = new(() => ReadStandardTable("charset-gb2312.txt"));
    static readonly Lazy<IReadOnlySet<uint>> Traditional = new(() => ReadStandardTable("charset-big5.txt"));

    static IReadOnlySet<uint> ReadStandardTable(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Witcher3FontTool.Resources." + file)
            ?? throw new InvalidDataException("Missing embedded character table: " + file);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().EnumerateRunes().Select(r => (uint)r.Value).ToHashSet();
    }

    public static List<uint> Create(FontCoverage coverage)
    {
        if (coverage == 0 || (coverage & ~All) != 0)
            throw new ArgumentException("Select at least one character set.", nameof(coverage));
        var result = new SortedSet<uint>();
        if ((coverage & FontCoverage.SimplifiedChinese) != 0) result.UnionWith(Simplified.Value);
        if ((coverage & FontCoverage.TraditionalChinese) != 0) result.UnionWith(Traditional.Value);
        if ((coverage & (FontCoverage.Russian | FontCoverage.Ukrainian)) != 0)
        {
            // Russian and Ukrainian alphabets, including Ё, Є, І, Ї, Ґ and lowercase forms.
            var cyrillic = new SortedSet<uint>(Enumerable.Range(0x410, 64).Select(c => (uint)c));
            if ((coverage & FontCoverage.Russian) != 0) cyrillic.UnionWith(new uint[] { 0x401, 0x451 });
            else cyrillic.ExceptWith(new uint[] { 0x42A, 0x42B, 0x42D, 0x44A, 0x44B, 0x44D });
            if ((coverage & FontCoverage.Ukrainian) != 0)
                cyrillic.UnionWith(new uint[] { 0x404, 0x406, 0x407, 0x454, 0x456, 0x457, 0x490, 0x491 });
            result.UnionWith(cyrillic);
        }
        if ((coverage & FontCoverage.English) != 0)
        {
            result.UnionWith(Enumerable.Range(0x20, 0x7F - 0x20).Select(c => (uint)c));
            result.UnionWith(Enumerable.Range(0xA0, 0x60).Select(c => (uint)c));
            result.Add(0x20AC); // Euro sign
        }
        return result.ToList();
    }

    public static string Describe(FontCoverage coverage, bool chinese) => chinese
        ? string.Join(" + ", new[] {
            (FontCoverage.SimplifiedChinese, "简体中文"),
            (FontCoverage.TraditionalChinese, "繁體中文"),
            (FontCoverage.English, "英文"), (FontCoverage.Russian, "俄语"), (FontCoverage.Ukrainian, "乌克兰语")
          }.Where(item => (coverage & item.Item1) != 0).Select(item => item.Item2))
        : string.Join(" + ", new[] {
            (FontCoverage.SimplifiedChinese, "Simplified Chinese"),
            (FontCoverage.TraditionalChinese, "Traditional Chinese"),
            (FontCoverage.English, "English"), (FontCoverage.Russian, "Russian"), (FontCoverage.Ukrainian, "Ukrainian")
          }.Where(item => (coverage & item.Item1) != 0).Select(item => item.Item2));
}

