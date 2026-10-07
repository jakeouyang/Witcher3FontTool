namespace Witcher3FontTool;

/// <summary>
/// Layout block (ascent / descent / leading, in twips with 20480 twips per em) of every font
/// slot in the game's original font libraries.
///
/// Why this exists: a <c>DefineFont3</c> carries the metrics Scaleform GFx uses to lay text
/// out. GFx puts the first baseline of a line at <c>top + ascent</c> and takes the line box
/// from ascent + descent + leading. The game's text fields were authored against the original
/// libraries, so replacing those numbers with the ones of an arbitrary user font moves text
/// around in every menu, subtitle and HUD label:
///
///   * Microsoft YaHei  0.81–1.06 em ascent, 1.32 em line box
///   * Source Han Sans  1.15 em ascent, 1.44 em line box
///   * 文鼎UD晶熙黑体G30_D (game, fonts_cn slot 1)  0.99 em ascent, 1.25 em line box
///
/// A bigger ascent pushes the baseline (and with it the whole line) down — the "text sinks
/// into the bottom of the frame" symptom. The values below are read from the game's own
/// <c>gameplay\gui_new\swf\witcher3\fonts_{cn,zh,en}.redswf</c>; keeping them makes the new
/// glyphs land exactly where the old ones did, whatever font the user picks.
///
/// Only the layout numbers are copied — no glyph or font data is taken from the game.
/// </summary>
public static class FontLayout
{
    /// <summary>Ascent / descent / leading of one font slot, in twips (20480 twips per em).</summary>
    public readonly record struct Metrics(short Ascent, short Descent, short Leading)
    {
        public double AscentEm => Ascent / 20480.0;
        public double DescentEm => Descent / 20480.0;
        public double LeadingEm => Leading / 20480.0;
    }

    // (language key used for template-{language}.swf, DefineFont3 FontID) -> original metrics
    static readonly Dictionary<(string Language, ushort FontId), Metrics> Original = new()
    {
        // fonts_cn.redswf — 文鼎UD晶熙黑体G30_D / PFDinTextCondPro-Regular
        [("cn", 1)] = new(20245, 4501, 853),
        [("cn", 5)] = new(18091, 4245, 853),
        // fonts_zh.redswf — Noto Sans TC Regular / PFDINTextCondPro-Regular
        [("zh", 1)] = new(17408, 3072, 853),
        [("zh", 5)] = new(19157, 5205, 853),
        // fonts_en.redswf — PF Din Text Cond Pro (regular / bold / italic)
        [("en", 1)] = new(18100, 4240, 1860),
        [("en", 3)] = new(18440, 4240, 2200),
        [("en", 5)] = new(18900, 4400, 2820),
    };

    /// <summary>
    /// Original layout of a slot, or <c>null</c> when the template carries a FontID that is not
    /// part of the shipped libraries (the caller then falls back to the font's own metrics).
    /// </summary>
    public static Metrics? For(string language, ushort fontId) =>
        Original.TryGetValue((language, fontId), out var metrics) ? metrics : null;
}
