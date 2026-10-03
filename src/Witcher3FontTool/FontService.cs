using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Witcher3FontTool;

public static class FontService
{
    public const string ModName = "modW3FontTool";
    const string Marker = "w3fonttool.json";
    sealed record Receipt(string Owner, Dictionary<string, string> Files);
    static readonly string[] Payload = { "content/codebayin.bundle", "content/metadata.store" };
    static byte[] Resource(string name)
    {
        using var stream = typeof(FontService).Assembly.GetManifestResourceStream("Witcher3FontTool.Resources." + name)
            ?? throw new InvalidDataException("缺少内置模板：" + name);
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    public static IEnumerable<(string Path, byte[] Swf)> ReadFontLibraries(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 32 || Encoding.ASCII.GetString(reader.ReadBytes(8)) != "POTATO70")
        { yield return (path, ReadSwf(path)); yield break; }
        stream.Position = 16; int indexBytes = reader.ReadInt32();
        if (indexBytes <= 0 || indexBytes % 320 != 0 || indexBytes > stream.Length - 32)
            throw new InvalidDataException("Unsupported bundle index.");
        for (int i = 0; i < indexBytes / 320; i++)
        {
            stream.Position = 32 + i * 320;
            string name = Encoding.ASCII.GetString(reader.ReadBytes(256)).TrimEnd('\0');
            stream.Position = 32 + i * 320 + 280;
            int size = reader.ReadInt32(), offset = reader.ReadInt32();
            if (size < 8 || offset < 32 + indexBytes || (long)offset + size > stream.Length)
                throw new InvalidDataException("Invalid bundle resource range.");
            stream.Position = offset;
            yield return (name, DecodeSwf(reader.ReadBytes(size)));
        }
    }
    public static byte[] ReadSwf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 4096 && Encoding.ASCII.GetString(bytes, 0, 8) == "POTATO70") bytes = bytes[4096..];
        return DecodeSwf(bytes);
    }
    static byte[] DecodeSwf(byte[] bytes)
    {
        if (bytes.Length < 8) throw new InvalidDataException("SWF 文件不完整");
        int size = checked((int)BitConverter.ToUInt32(bytes, 4));
        if (size < 8 || size > 256 * 1024 * 1024) throw new InvalidDataException("SWF 大小无效");
        if (Encoding.ASCII.GetString(bytes, 0, 3) == "CWS")
        {
            using var source = new MemoryStream(bytes, 8, bytes.Length - 8);
            using var z = new ZLibStream(source, CompressionMode.Decompress);
            var plain = new byte[size]; bytes.AsSpan(0, 8).CopyTo(plain); plain[0] = (byte)'F';
            z.ReadExactly(plain.AsSpan(8)); return plain;
        }
        if (Encoding.ASCII.GetString(bytes, 0, 3) != "FWS" || size > bytes.Length) throw new InvalidDataException("SWF 格式无效");
        return bytes[..size];
    }
    public static void Generate(string fontPath, string output, Action<string> report,
        FontCoverage coverage = FontCoverage.SimplifiedChinese | FontCoverage.English, bool chinese = false)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException(chinese ? "输出目录已存在，请选择新目录。" : "Output directory already exists; choose a new directory.");
        report(chinese ? "正在读取并验证字体…" : "Reading and validating font…");
        OutlineFont font;
        try { font = new OutlineFont(fontPath); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDataException(chinese
                ? $"无法解析此字体文件。它可能已损坏、不受支持，或缺少 TrueType/CFF 轮廓数据。详细信息：{ex.Message}"
                : $"Could not parse this font file. It may be damaged, unsupported, or lack TrueType/CFF outlines. Details: {ex.Message}", ex);
        }
        // Validate the selection before creating any output.
        _ = FontCharacterSets.Create(coverage);
        var resources = new List<BundleWriter.ResourceEntry>();
        foreach (var (selection, language) in new[] {
            (FontCoverage.SimplifiedChinese, "cn"), (FontCoverage.TraditionalChinese, "zh"), (FontCoverage.English, "en") })
        {
            if ((coverage & selection) == 0) continue;
            var requested = new SortedSet<uint>(FontCharacterSets.Create(selection | FontCoverage.English));
            requested.UnionWith(Encoding.UTF8.GetString(Resource($"charset-game-{language}.txt")).EnumerateRunes().Select(r => (uint)r.Value));
            var codes = requested.Where(c => font.GlyphIndex(c) != 0).ToList();
            if (codes.Count == 0 || (language != "en" && !codes.Any(c => c >= 0x3400 && c <= 0x9FFF)))
                throw new InvalidDataException($"{FontCharacterSets.Describe(selection, chinese)}: " + (chinese ? "所选字体没有该语言的字形。" : "The selected font has no glyphs for this language."));
            report($"{FontCharacterSets.Describe(selection, chinese)} — {font.Family}: {codes.Count:N0}/{requested.Count:N0}");
            var missing = requested.Except(codes).ToArray();
            if (missing.Length > 0)
            {
                var samples = missing.Where(c => c >= 0x3000).Concat(missing.Where(c => c < 0x3000)).Take(12).Select(c => char.ConvertFromUtf32((int)c));
                report(chinese ? $"缺字 {missing.Length:N0} 个（不会生成缺失字形），例如：{string.Concat(samples)}" : $"Missing {missing.Length:N0} characters (not embedded), e.g. {string.Concat(samples)}");
            }
            var template = new TemplateSwf(Resource($"template-{language}.swf"));
            var replacements = new Dictionary<ushort, byte[]>();
            foreach (var original in template.Tags.Where(t => t.Code == 75).Select(t => t.Data))
            {
                ushort id = BitConverter.ToUInt16(original);
                // Preserve all family registrations and bold/italic identities, replacing every slot.
                var tag = DefineFont3Builder.Build(font, new DefineFont3Builder.Options
                {
                    FontId = id,
                    FontName = Encoding.UTF8.GetString(original, 5, original[4]),
                    LanguageCode = original[3],
                    TwipsPerUnit = DefineFont3Builder.PickScale(font)
                }, codes, out var stats);
                tag[2] = (byte)((tag[2] & ~3) | (original[2] & 3));
                int table = 7 + tag[4];
                for (int i = 0; i < codes.Count; i++)
                {
                    int begin = checked((int)BitConverter.ToUInt32(tag, table + i * 4));
                    int end = checked((int)BitConverter.ToUInt32(tag, table + (i + 1) * 4));
                    SwfShapeDecoder.DecodeShape(tag, table + begin, end - begin);
                }
                replacements.Add(id, tag);
            }
            var swf = template.Rebuild(replacements);
            using var packed = new MemoryStream();
            packed.Write(swf, 0, 8); packed.Position = 0; packed.WriteByte((byte)'C'); packed.Position = 8;
            using (var z = new ZLibStream(packed, CompressionLevel.SmallestSize, true)) z.Write(swf, 8, swf.Length - 8);
            resources.Add(new BundleWriter.ResourceEntry($@"gameplay\gui_new\swf\witcher3\fonts_{language}.redswf", packed.ToArray()));
            report(chinese ? $"fonts_{language}：已替换 {replacements.Count} 个字体槽位，压缩后 {packed.Length / 1048576.0:F1} MB。" : $"fonts_{language}: replaced {replacements.Count} font slots, {packed.Length / 1048576.0:F1} MB compressed.");
        }
        BundleWriter.WriteMod(resources, output);
        var receipt = new Receipt("W3FontTool/1", Payload.ToDictionary(p => p, p => Hash(Path.Combine(output, p))));
        File.WriteAllText(Path.Combine(output, Marker), JsonSerializer.Serialize(receipt));
        report(chinese ? $"MOD 已生成：{output}" : $"MOD generated: {output}");
    }
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static void NoLinks(string path)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(path)); d != null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("不支持符号链接或目录联接：" + d.FullName);
    }
    public static void Validate(string game)
    {
        if (BundleWriter.ValidateGameDir(game) is { } error) throw new InvalidDataException(error);
        if (Process.GetProcessesByName("witcher3").Length > 0) throw new IOException("Exit The Witcher 3 before installing or restoring the mod.");
        NoLinks(game); NoLinks(Path.Combine(game, "Mods")); NoLinks(Path.Combine(game, ".W3FontTool"));
    }
    static void CheckOwned(string path)
    {
        NoLinks(path);
        foreach (var item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new IOException("MOD 内含链接，已停止操作。");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(Path.Combine(path, Marker)));
        if (receipt?.Owner != "W3FontTool/1" || receipt.Files == null || receipt.Files.Count != Payload.Length)
            throw new IOException("目标 MOD 不属于本工具，已停止操作。");
        foreach (var relative in Payload)
            if (!receipt.Files.TryGetValue(relative, out var hash) || Hash(Path.Combine(path, relative)) != hash)
                throw new IOException("工具 MOD 已被外部修改，已停止操作并保留文件。");
        var expected = Payload.Append(Marker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(p => !expected.Contains(Path.GetRelativePath(path, p).Replace('\\', '/'))))
            throw new IOException("工具 MOD 中存在额外文件，已停止操作并保留文件。");
    }
    static void CheckConflicts(string mods, FontCoverage coverage)
    {
        if (!Directory.Exists(mods)) return;
        foreach (var dir in Directory.GetDirectories(mods))
        {
            if (Path.GetFileName(dir).Equals(ModName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Path.GetFileName(dir).StartsWith("mod", StringComparison.OrdinalIgnoreCase)) continue;
            NoLinks(dir);
            foreach (var file in Directory.EnumerateFiles(dir, "*.bundle", SearchOption.AllDirectories))
            {
                using var stream = File.OpenRead(file);
                var header = new byte[Math.Min(65536, stream.Length)]; stream.ReadExactly(header);
                if (new[] { (FontCoverage.SimplifiedChinese, "cn"), (FontCoverage.TraditionalChinese, "zh"), (FontCoverage.English, "en") }
                    .Any(e => (coverage & e.Item1) != 0 && Encoding.ASCII.GetString(header).Contains($"fonts_{e.Item2}.redswf", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException($"发现与所选语言冲突的字体 MOD：{Path.GetFileName(dir)}。请先将它移出 Mods，再生成；本工具不会改动它。");
            }
        }
    }
    static string BackupPath(string game)
    {
        var root = Path.Combine(game, ".W3FontTool", "backups"); NoLinks(root); Directory.CreateDirectory(root);
        return Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
    }
    static FileStream LockGame(string game)
    {
        var state = Path.Combine(game, ".W3FontTool");
        Directory.CreateDirectory(state);
        string path = Path.Combine(state, "operation.lock");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("操作锁是链接，已停止。");
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("此游戏目录正在被另一工具实例操作，或目录不可写。", ex); }
    }
    public static void Install(string game, string font, Action<string> report,
        FontCoverage coverage = FontCoverage.SimplifiedChinese | FontCoverage.English, bool chinese = false)
    {
        game = Path.GetFullPath(game); Validate(game);
        using var operation = LockGame(game);
        var mods = Path.Combine(game, "Mods"); var target = Path.Combine(mods, ModName);
        CheckConflicts(mods, coverage);
        if (Directory.Exists(target)) CheckOwned(target);
        var work = Path.Combine(game, ".W3FontTool", "build-" + Guid.NewGuid().ToString("N"));
        Generate(font, work, report, coverage, chinese);
        Validate(game); CheckConflicts(mods, coverage);
        Directory.CreateDirectory(mods);
        string? backup = null;
        if (Directory.Exists(target)) { CheckOwned(target); backup = BackupPath(game); Directory.Move(target, backup); }
        try { Directory.Move(work, target); }
        catch { if (backup != null && !Directory.Exists(target)) Directory.Move(backup, target); throw; }
        report($"Installed: {target}");
        if (backup != null) report($"Previous version backed up: {backup}");
        report("Check in-game menus and subtitles in the selected language.");
    }
    public static void Restore(string game, Action<string> report)
    {
        game = Path.GetFullPath(game); Validate(game);
        using var operation = LockGame(game);
        var target = Path.Combine(game, "Mods", ModName);
        if (!Directory.Exists(target)) { report("No W3FontTool mod is installed; nothing to restore."); return; }
        CheckOwned(target); var backup = BackupPath(game); Directory.Move(target, backup);
        report("W3FontTool mod disabled. The game's original fonts are active again; backup kept at: " + backup);
    }
}

