namespace Witcher3FontTool;

internal static class SelfTest
{
    public static void Run(string root)
    {
        var hans = FontCharacterSets.Create(FontCoverage.SimplifiedChinese);
        var hant = FontCharacterSets.Create(FontCoverage.TraditionalChinese);
        var latin = FontCharacterSets.Create(FontCoverage.English);
        if (!hans.Contains('游') || hans.Contains('遊') || !hant.Contains('遊') || !latin.Contains('W') || !latin.Contains('é'))
            throw new Exception("Selected language character sets failed validation");
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Test directory must be new");
        string preferenceFile = Path.Combine(root, "preferences", "settings.json");
        UserPreferences.SaveToPath(preferenceFile, "C:\\Games\\The Witcher 3", "C:\\Fonts\\example.ttf", FontCoverage.TraditionalChinese | FontCoverage.English, chineseInterface: true);
        var savedPreferences = UserPreferences.LoadFromPath(preferenceFile);
        if (savedPreferences.GamePath != "C:\\Games\\The Witcher 3" || savedPreferences.FontPath != "C:\\Fonts\\example.ttf" ||
            savedPreferences.Coverage != (FontCoverage.TraditionalChinese | FontCoverage.English) || !savedPreferences.ChineseInterface)
            throw new Exception("Preference persistence failed.");
        Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
        File.WriteAllText(Path.Combine(root, "bin", "x64", "witcher3.exe"), "test");
        Directory.CreateDirectory(Path.Combine(root, "content", "content0", "bundles"));
        File.WriteAllText(Path.Combine(root, "content", "content0", "bundles", "r4gui.bundle"), "test");
        string font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "simhei.ttf");
        Task.Run(() => FontService.Install(root, font, Console.WriteLine, (FontCoverage)7)).GetAwaiter().GetResult();
        var bundle = Path.Combine(root, "Mods", FontService.ModName, "content", "codebayin.bundle");
        Program.Verify(bundle);
        var libraries = FontService.ReadFontLibraries(bundle).ToArray();
        if (libraries.Length != 3 || !libraries.Select(l => l.Path.Split('\\')[^1]).Order().SequenceEqual(new[] { "fonts_cn.redswf", "fonts_en.redswf", "fonts_zh.redswf" }))
            throw new Exception("Missing selected language library.");
        foreach (var library in libraries)
        {
            var tags = new TemplateSwf(library.Swf).Tags.Where(t => t.Code == 75).ToArray();
            if (tags.Length != (library.Path.Contains("fonts_en") ? 3 : 2)) throw new Exception("Missing font slots.");
            foreach (var tag in tags)
            {
                var outlines = SwfShapeDecoder.Decode(tag.Data, new uint[] { 'W', '3' });
                if (outlines.Count != 2) throw new Exception("A font slot lacks supported English characters.");
            }
        }
        string other = Path.Combine(root, "Mods", "modOther", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!); File.WriteAllText(other, "untouched");
        FontService.Install(root, font, Console.WriteLine, (FontCoverage)7);
        string extra = Path.Combine(root, "Mods", FontService.ModName, "extra.txt");
        File.WriteAllText(extra, "external");
        ExpectFailure(() => FontService.Restore(root, Console.WriteLine));
        if (!File.Exists(extra)) throw new Exception("Extra file changed");
        File.Move(extra, Path.Combine(root, "extra.saved"));
        byte[] validBundle = File.ReadAllBytes(bundle);
        File.AppendAllText(bundle, "tampered");
        ExpectFailure(() => FontService.Restore(root, Console.WriteLine));
        File.WriteAllBytes(bundle, validBundle);
        FontService.Restore(root, Console.WriteLine);
        if (File.ReadAllText(other) != "untouched" || Directory.Exists(Path.Combine(root, "Mods", FontService.ModName))) throw new Exception("Restore failed");
        FontService.Restore(root, Console.WriteLine);
        string conflict = Path.Combine(root, "Mods", "modConflict", "content");
        Directory.CreateDirectory(conflict);
        File.WriteAllBytes(Path.Combine(conflict, "font.bundle"), validBundle);
        ExpectFailure(() => FontService.Install(root, font, Console.WriteLine, (FontCoverage)7));
        if (!File.ReadAllBytes(Path.Combine(conflict, "font.bundle")).SequenceEqual(validBundle)) throw new Exception("Conflict changed");
        string target = Path.Combine(root, "Mods", FontService.ModName);
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "foreign.txt"), "keep");
        try { FontService.Restore(root, Console.WriteLine); throw new Exception("Foreign mod was accepted"); }
        catch (IOException) { }
        if (!File.Exists(Path.Combine(target, "foreign.txt"))) throw new Exception("Foreign mod changed");
        File.WriteAllText(Path.Combine(root, "PASS.txt"), "Install, all glyphs, reinstall, restore, other-mod preservation, foreign-mod refusal: PASS");
    }
    static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        throw new Exception("Expected operation refusal");
    }
}
