namespace Witcher3FontTool;

/// <summary>Checks source routing and the actual generated Cyrillic/combined font libraries.</summary>
internal static class FeatureTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Test directory must be new.");
        Routing();
        var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var primary = new OutlineFont(Path.Combine(fontDir, "arial.ttf"));
        var latin = new OutlineFont(Path.Combine(fontDir, "times.ttf"));
        var composite = new CompositeFont(primary, latin);
        var samples = "W3é,.ЖЁЄІЇҐ".Select(c => (uint)c).Order().ToArray();
        var options = new DefineFont3Builder.Options { TwipsPerUnit = DefineFont3Builder.PickScale(primary), Layout = FontLayout.For("ru", 1) };
        var combined = DefineFont3Builder.Build(composite, options, samples, out _);
        var fromPrimary = DefineFont3Builder.Build(primary, options, samples, out _);
        var fromLatin = DefineFont3Builder.Build(latin, options, samples, out _);
        foreach (uint code in samples)
        {
            var expected = CompositeFont.IsLatinCharacter(code) ? fromLatin : fromPrimary;
            if (!Shape(combined, code).SequenceEqual(Shape(expected, code)) || Advance(combined, code) != Advance(expected, code))
                throw new Exception($"Wrong glyph outline or advance source for U+{code:X4}.");
        }
        var output = Path.Combine(root, "mixed");
        var coverage = FontCoverage.English | FontCoverage.Russian | FontCoverage.Ukrainian;
        FontService.Generate(Path.Combine(fontDir, "arial.ttf"), output, Console.WriteLine, coverage, latinFontPath: Path.Combine(fontDir, "times.ttf"));
        var bundle = Path.Combine(output, "content", "codebayin.bundle");
        Program.Verify(bundle);
        var libraries = FontService.ReadFontLibraries(bundle).ToArray();
        if (libraries.Length != 3 || !libraries.Select(l => l.Path.Split('\\')[^1]).Order().SequenceEqual(new[] { "fonts_en.redswf", "fonts_ru.redswf", "fonts_ua.redswf" }))
            throw new Exception("Wrong selected game resources.");
        foreach (var library in libraries)
        {
            string key = library.Path.Split('\\')[^1][6..8];
            var template = new TemplateSwf(library.Swf);
            if (template.Font3TagCount != 3) throw new Exception("Not all font slots were replaced.");
            foreach (var tag in template.Tags.Where(t => t.Code == 75))
            {
                if (DefineFont3Builder.ReadLayout(tag.Data) != FontLayout.For(key, BitConverter.ToUInt16(tag.Data)))
                    throw new Exception("Game line metrics changed.");
                if (!Shape(tag.Data, 'W').SequenceEqual(Shape(fromLatin, 'W')) || Advance(tag.Data, 'W') != Advance(fromLatin, 'W'))
                    throw new Exception("A generated slot did not use the Latin font.");
                if (key != "en" && !Shape(tag.Data, 'Ё').SequenceEqual(Shape(fromPrimary, 'Ё')))
                    throw new Exception("A Cyrillic slot used the wrong font.");
            }
        }
        string chinesePath = Path.Combine(fontDir, "simhei.ttf");
        if (File.Exists(chinesePath))
        {
            var chinese = new OutlineFont(chinesePath);
            var chineseTag = DefineFont3Builder.Build(chinese, options, new uint[] { '中', '，' }, out _);
            string chineseOutput = Path.Combine(root, "chinese-mixed");
            FontService.Generate(chinesePath, chineseOutput, Console.WriteLine, FontCoverage.SimplifiedChinese, latinFontPath: Path.Combine(fontDir, "times.ttf"));
            foreach (var slot in FontService.ReadFontLibraries(Path.Combine(chineseOutput, "content", "codebayin.bundle"))
                         .SelectMany(l => new TemplateSwf(l.Swf).Tags.Where(t => t.Code == 75)))
                if (!Shape(slot.Data, '中').SequenceEqual(Shape(chineseTag, '中')) ||
                    !Shape(slot.Data, '，').SequenceEqual(Shape(chineseTag, '，')) ||
                    !Shape(slot.Data, '3').SequenceEqual(Shape(fromLatin, '3')))
                    throw new Exception("Chinese/Latin composition failed inside a Chinese font slot.");
        }
        string unsupportedFont = Path.Combine(fontDir, "wingding.ttf");
        if (File.Exists(unsupportedFont))
        {
            string refused = Path.Combine(root, "unsupported-cyrillic");
            bool rejected = false;
            try { FontService.Generate(unsupportedFont, refused, _ => { }, FontCoverage.Russian); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected || Directory.Exists(refused)) throw new Exception("A font lacking Cyrillic letters was accepted.");
        }
        // Old settings remain readable; new paths and all language flags round-trip, including all unchecked.
        string preferences = Path.Combine(root, "settings.json");
        UserPreferences.SaveToPath(preferences, "game", "primary", FontCharacterSets.All, true, "latin");
        var saved = UserPreferences.LoadFromPath(preferences);
        if (saved.LatinFontPath != "latin" || saved.Coverage != FontCharacterSets.All || !saved.ChineseInterface)
            throw new Exception("New preferences were not restored.");
        UserPreferences.SaveToPath(preferences, "game", "primary", 0, false, "");
        if (UserPreferences.LoadFromPath(preferences).Coverage != 0) throw new Exception("All-unchecked preferences were lost.");
        File.WriteAllText(preferences, "{\"GamePath\":\"old\",\"FontPath\":\"font\",\"Coverage\":5,\"ChineseInterface\":true}");
        saved = UserPreferences.LoadFromPath(preferences);
        if (saved.GamePath != "old" || saved.LatinFontPath != null || saved.Coverage != (FontCoverage)5)
            throw new Exception("Stable-version preferences are no longer compatible.");
        File.WriteAllText(preferences, "broken json");
        if (UserPreferences.LoadFromPath(preferences).Coverage != (FontCoverage)5) throw new Exception("Damaged preferences prevented default recovery.");
        File.WriteAllText(Path.Combine(root, "PASS.txt"), "Composite glyph sources, advances, fallbacks, Cyrillic resources/slots, game layout and preferences: PASS");
    }
    static void Routing()
    {
        var primary = new SampleFont(100, new uint[] { '中', 'W', 'A', 'Ё' });
        var latin = new SampleFont(500, new uint[] { 'W', '3', '遊' });
        var combined = new CompositeFont(primary, latin);
        if (combined.AdvanceOf(combined.GlyphIndex('中')) != 100 || combined.AdvanceOf(combined.GlyphIndex('W')) != 500 ||
            combined.GlyphIndex('中') == combined.GlyphIndex('W')) throw new Exception("Source glyph IDs collided.");
        if (!combined.UsesFallback('遊') || !combined.UsesLatinFont('遊') || !combined.UsesFallback('A') ||
            combined.UsesLatinFont('A') || combined.UsesLatinFont('Ё') || combined.GlyphIndex('無') != 0) throw new Exception("Fallback routing failed.");
        var single = new CompositeFont(primary);
        if (single.GlyphIndex('3') != 0 || single.AdvanceOf(single.GlyphIndex('W')) != 100 || single.UsesFallback('W'))
            throw new Exception("Single-font compatibility failed.");
        if (CompositeFont.IsLatinCharacter('，') || CompositeFont.IsLatinCharacter('Ж') || !CompositeFont.IsLatinCharacter('é'))
            throw new Exception("Character classes were confused.");
    }
    static (int Table, int Count, int CodeAt, int Index) Find(byte[] tag, uint code)
    {
        int table = 7 + tag[4], count = BitConverter.ToUInt16(tag, table - 2);
        int codeAt = table + (int)BitConverter.ToUInt32(tag, table + count * 4);
        for (int i = 0; i < count; i++) if (BitConverter.ToUInt16(tag, codeAt + i * 2) == code) return (table, count, codeAt, i);
        throw new Exception($"Missing U+{code:X4} in generated font.");
    }
    internal static byte[] Shape(byte[] tag, uint code)
    {
        var p = Find(tag, code);
        int start = p.Table + (int)BitConverter.ToUInt32(tag, p.Table + p.Index * 4);
        int end = p.Table + (int)BitConverter.ToUInt32(tag, p.Table + (p.Index + 1) * 4);
        return tag[start..end];
    }
    static short Advance(byte[] tag, uint code) { var p = Find(tag, code); return BitConverter.ToInt16(tag, p.CodeAt + p.Count * 2 + 6 + p.Index * 2); }
    sealed class SampleFont(int advance, uint[] codes) : IOutlineFont
    {
        public ushort UnitsPerEm => 16384;
        public short Ascender => 12000;
        public short Descender => -4000;
        public short LineGap => 0;
        public ushort GlyphIndex(uint code) => codes.Contains(code) ? (ushort)1 : (ushort)0;
        public int AdvanceOf(ushort glyph) => advance;
        public GlyphOutline? GetGlyphOutline(ushort glyph, int depth = 0) => null;
    }
}
