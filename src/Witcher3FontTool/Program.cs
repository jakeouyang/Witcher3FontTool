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
                case ["install", var game, var font, .. var options]: FontService.Install(game, font, Console.WriteLine, ParseCoverage(options)); break;
                case ["restore", var game]: FontService.Restore(game, Console.WriteLine); break;
                case ["generate", var font, var output, .. var options]: FontService.Generate(font, output, Console.WriteLine, ParseCoverage(options)); break;
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
                default: throw new ArgumentException("Commands: install <game> <font> [--zh-hans] [--zh-hant] [--english] | restore <game> | generate <font> <new-directory> [--zh-hans] [--zh-hant] [--english] | verify <bundle/swf> | layout <bundle/swf> | render <bundle/swf> <text> | validate-game <game>");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    static FontCoverage ParseCoverage(string[] options)
    {
        if (options.Length == 0) return FontCoverage.SimplifiedChinese | FontCoverage.English;
        FontCoverage coverage = 0;
        foreach (var option in options)
            coverage |= option switch
            {
                "--zh-hans" => FontCoverage.SimplifiedChinese,
                "--zh-hant" => FontCoverage.TraditionalChinese,
                "--english" => FontCoverage.English,
                _ => throw new ArgumentException("Unknown character-set option: " + option)
            };
        if (coverage == 0) throw new ArgumentException("Select at least one character set.");
        return coverage;
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
