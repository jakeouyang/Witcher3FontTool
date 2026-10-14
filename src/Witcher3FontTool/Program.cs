using System.Runtime.InteropServices;
namespace Witcher3FontTool;
internal static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 0) { Application.Run(new MainForm()); return 0; }
        AttachConsole(-1);
        try
        {
            switch (args)
            {
                case ["install", var game, var font, .. var options]:
                {
                    var config = ParseOptions(options);
                    FontService.Install(game, font, Console.WriteLine, config.Coverage, latinFontPath: config.LatinFont); break;
                }
                case ["restore", var game]: FontService.Restore(game, Console.WriteLine); break;
                case ["generate", var font, var output, .. var options]:
                {
                    var config = ParseOptions(options);
                    FontService.Generate(font, output, Console.WriteLine, config.Coverage, latinFontPath: config.LatinFont); break;
                }
                case ["validate-game", var game]:
                    if (BundleWriter.ValidateGameDir(game) is { } error) throw new InvalidDataException(error);
                    Console.WriteLine("VALID"); break;
                case ["verify", var input]: Verify(input); break;
                case ["layout", var input]: Layout(input); break;
                case ["render", var input, var text]:
                    var tag = new TemplateSwf(FontService.ReadSwf(input)).Tags.First(t => t.Code == 75);
                    foreach (var pair in SwfShapeDecoder.Decode(tag.Data, text.Select(c => (uint)c)))
                        Console.WriteLine($"{(char)pair.Key}\n{GlyphRasterizer.RenderAscii(pair.Value)}");
                    break;
                case ["--render-ui", var output, .. var options]:
                    using (var form = new MainForm())
                    {
                        form.SelectInterfaceLanguage(options.Contains("--zh"), save: false);
                        form.Show(); Application.DoEvents();
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(output);
                    }
                    break;
                case ["--self-test", var output]: SelfTest.Run(output); break;
                case ["--feature-test", var output]: FeatureTest.Run(output); break;
                default: throw new ArgumentException("Commands: install <game> <font> [--zh-hans] [--zh-hant] [--english] [--russian] [--ukrainian] [--latin-font <font>] | restore <game> | generate <font> <new-directory> [same options] | verify <bundle/swf> | layout <bundle/swf> | render <bundle/swf> <text> | validate-game <game>");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    static (FontCoverage Coverage, string? LatinFont) ParseOptions(string[] options)
    {
        FontCoverage coverage = 0;
        string? latin = null;
        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            if (option == "--latin-font")
            {
                if (latin != null || ++i >= options.Length || string.IsNullOrWhiteSpace(options[i]) || options[i].StartsWith("--"))
                    throw new ArgumentException("Specify exactly one font path after --latin-font.");
                latin = options[i]; continue;
            }
            coverage |= option switch
            {
                "--zh-hans" => FontCoverage.SimplifiedChinese,
                "--zh-hant" => FontCoverage.TraditionalChinese,
                "--english" => FontCoverage.English,
                "--russian" => FontCoverage.Russian,
                "--ukrainian" => FontCoverage.Ukrainian,
                _ => throw new ArgumentException("Unknown character-set option: " + option)
            };
        }
        return (coverage == 0 ? FontCoverage.SimplifiedChinese | FontCoverage.English : coverage, latin);
    }
    internal static void Verify(string path)
    {
        foreach (var library in FontService.ReadFontLibraries(path))
        foreach (var tag in new TemplateSwf(library.Swf).Tags.Where(t => t.Code == 75))
        {
            var data = tag.Data;
            int n = BitConverter.ToUInt16(data, 5 + data[4]), table = 7 + data[4];
            for (int i = 0; i < n; i++)
            {
                int begin = checked((int)BitConverter.ToUInt32(data, table + 4 * i));
                int end = checked((int)BitConverter.ToUInt32(data, table + 4 * (i + 1)));
                SwfShapeDecoder.DecodeShape(data, table + begin, end - begin);
            }
            Console.WriteLine($"PASS: {library.Path} / font {BitConverter.ToUInt16(data)} / {n} glyphs");
        }
    }
    /// <summary>Prints the layout (ascent/descent/leading) of every font slot, in twips and em.</summary>
    internal static void Layout(string path)
    {
        foreach (var library in FontService.ReadFontLibraries(path))
        foreach (var tag in new TemplateSwf(library.Swf).Tags.Where(t => t.Code == 75))
        {
            var metrics = DefineFont3Builder.ReadLayout(tag.Data);
            Console.WriteLine($"{library.Path} / font {BitConverter.ToUInt16(tag.Data, 0)}: " +
                $"ascent={metrics.Ascent} descent={metrics.Descent} leading={metrics.Leading} twips " +
                $"({metrics.AscentEm:F4} / {metrics.DescentEm:F4} / {metrics.LeadingEm:F4} em)");
        }
    }
}
