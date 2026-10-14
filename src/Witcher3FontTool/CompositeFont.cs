namespace Witcher3FontTool;

/// <summary>Routes Unicode characters to two fonts, using distinct virtual glyph IDs.</summary>
public sealed class CompositeFont : IOutlineFont
{
    readonly IOutlineFont primary;
    readonly IOutlineFont? latin;
    readonly Dictionary<uint, ushort> codes = new();
    readonly List<(IOutlineFont Font, ushort Glyph)> glyphs = new() { (null!, 0) };
    readonly Dictionary<(IOutlineFont Font, ushort Glyph), ushort> ids = new();

    public CompositeFont(IOutlineFont primary, IOutlineFont? latin = null)
    {
        this.primary = primary; this.latin = latin;
        if (latin != null && latin.UnitsPerEm != primary.UnitsPerEm)
            throw new ArgumentException("The font sources must use the same outline units.");
    }
    public ushort UnitsPerEm => primary.UnitsPerEm;
    public short Ascender => primary.Ascender;
    public short Descender => primary.Descender;
    public short LineGap => primary.LineGap;

    // Latin letters, digits, halfwidth punctuation, combining accents, currency and common symbols.
    // CJK punctuation/fullwidth characters and Cyrillic letters keep the primary font preference.
    public static bool IsLatinCharacter(uint code) =>
        code is >= 0x20 and <= 0x024F or >= 0x0300 and <= 0x036F or >= 0x1E00 and <= 0x1EFF
            or >= 0x2000 and <= 0x218F;

    (IOutlineFont Font, ushort Glyph) Resolve(uint code)
    {
        var preferred = latin != null && IsLatinCharacter(code) ? latin : primary;
        ushort glyph = preferred.GlyphIndex(code);
        if (glyph != 0) return (preferred, glyph);
        var fallback = ReferenceEquals(preferred, primary) ? latin : primary;
        return fallback != null ? (fallback, fallback.GlyphIndex(code)) : (preferred, (ushort)0);
    }
    public bool UsesLatinFont(uint code) => latin != null && ReferenceEquals(Resolve(code).Font, latin) && Resolve(code).Glyph != 0;
    public bool UsesFallback(uint code)
    {
        var resolved = Resolve(code);
        var preferred = latin != null && IsLatinCharacter(code) ? latin : primary;
        return resolved.Glyph != 0 && !ReferenceEquals(preferred, resolved.Font);
    }
    public ushort GlyphIndex(uint code)
    {
        if (codes.TryGetValue(code, out var id)) return id;
        var source = Resolve(code);
        if (source.Glyph == 0) return codes[code] = 0;
        if (!ids.TryGetValue(source, out id))
        {
            if (glyphs.Count > ushort.MaxValue) throw new InvalidDataException("Too many distinct glyphs in the combined font.");
            id = (ushort)glyphs.Count; glyphs.Add(source); ids.Add(source, id);
        }
        return codes[code] = id;
    }
    public int AdvanceOf(ushort glyph) { var source = glyphs[glyph]; return source.Font.AdvanceOf(source.Glyph); }
    public GlyphOutline? GetGlyphOutline(ushort glyph, int depth = 0)
    { var source = glyphs[glyph]; return source.Font.GetGlyphOutline(source.Glyph, depth); }
}
