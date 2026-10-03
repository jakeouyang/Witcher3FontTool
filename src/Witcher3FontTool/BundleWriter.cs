using System.Text;

namespace Witcher3FontTool;

/// <summary>Uncompressed legacy POTATO70 bundle with an explicit metadata index.</summary>
public static class BundleWriter
{
    public sealed record ResourceEntry(string Path, byte[] Swf);
    public static void WriteMod(IReadOnlyList<ResourceEntry> resources, string outputModDir)
    {
        if (resources.Count == 0) throw new ArgumentException("No resources selected.");
        const int dataStart = 4096;
        var offsets = new int[resources.Count];
        int length = dataStart;
        for (int i = 0; i < resources.Count; i++)
        {
            offsets[i] = length;
            length = checked((length + resources[i].Swf.Length + 15) / 16 * 16);
        }
        var content = Path.Combine(outputModDir, "content");
        Directory.CreateDirectory(content);
        using (var stream = File.Create(Path.Combine(content, "codebayin.bundle")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("POTATO70"));
            writer.Write(length); writer.Write(0); writer.Write(resources.Count * 320);
            writer.Write(0x00010003); writer.Write(0L);
            for (int i = 0; i < resources.Count; i++)
            {
                var entry = resources[i];
                var name = new byte[256]; Encoding.ASCII.GetBytes(entry.Path).CopyTo(name, 0);
                writer.Write(name); writer.Write(new byte[16]); writer.Write(0);
                writer.Write(entry.Swf.Length); writer.Write(entry.Swf.Length); writer.Write(offsets[i]);
                writer.Write(new byte[24]); writer.Write(Crc32(entry.Swf)); writer.Write(0);
            }
            for (int i = 0; i < resources.Count; i++)
            {
                stream.Position = offsets[i]; writer.Write(resources[i].Swf);
            }
            stream.SetLength(length);
        }
        File.WriteAllBytes(Path.Combine(content, "metadata.store"), Metadata(resources, offsets, length));
    }

    static byte[] Metadata(IReadOnlyList<ResourceEntry> entries, int[] offsets, int bundleLength)
    {
        using var strings = new MemoryStream();
        int Add(string value) { int at = (int)strings.Position; strings.Write(Encoding.ASCII.GetBytes(value)); strings.WriteByte(0); return at; }
        Add(""); int bundleName = Add("codebayin.bundle");
        var paths = entries.Select(e => Add(e.Path)).ToArray();
        var dirs = new[] { Add(""), Add("gameplay"), Add("gui_new"), Add("swf"), Add("witcher3") };
        var names = entries.Select(e => Add(e.Path.Split('\\')[^1])).ToArray();
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        w.Write(new byte[] { 3, 0x56, 0x54, 0x4D }); w.Write(6);
        int max = entries.Max(e => e.Swf.Length); w.Write(max); w.Write(max);
        Vlq(w, (int)strings.Length); w.Write(strings.ToArray());
        Vlq(w, entries.Count + 1); w.Write(new byte[24]); w.Write(0x02100000); w.Write(0);
        for (int i = 0; i < entries.Count; i++)
        {
            w.Write(paths[i]); w.Write(0); w.Write(entries[i].Swf.Length); w.Write(entries[i].Swf.Length);
            w.Write(i + 1); w.Write(0); w.Write(0); w.Write(0);
        }
        Vlq(w, entries.Count + 1); w.Write(new byte[20]);
        for (int i = 0; i < entries.Count; i++)
        { w.Write(i + 1); w.Write(1); w.Write(offsets[i]); w.Write(entries[i].Swf.Length); w.Write(0); }
        Vlq(w, 2); w.Write(new byte[24]);
        w.Write(bundleName); w.Write(1); w.Write(entries.Count);
        int indexEnd = 32 + 320 * entries.Count;
        w.Write(bundleLength - indexEnd); w.Write(indexEnd); w.Write(0);
        Vlq(w, 0); Vlq(w, dirs.Length);
        for (int i = 0; i < dirs.Length; i++) { w.Write(dirs[i]); w.Write(Math.Max(0, i - 1)); }
        Vlq(w, entries.Count);
        for (int i = 0; i < entries.Count; i++) { w.Write(i + 1); w.Write(4); w.Write(names[i]); }
        Vlq(w, entries.Count);
        foreach (var entry in entries.Select((e, i) => (Hash: PathHash(e.Path), Id: i + 1)).OrderBy(e => e.Hash))
        { w.Write(entry.Hash); w.Write((long)entry.Id); }
        return output.ToArray();
    }
    static void Vlq(BinaryWriter writer, int n)
    {
        writer.Write((byte)((n & 63) | (n >= 64 ? 64 : 0))); n >>= 6;
        while (n > 0) { writer.Write((byte)((n & 127) | (n >= 128 ? 128 : 0))); n >>= 7; }
    }
    static ulong PathHash(string path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.ASCII.GetBytes(path.ToLowerInvariant())) hash = unchecked((hash ^ b) * 1099511628211);
        return hash;
    }
    static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
        return ~crc;
    }

    // ───────────────────────── game-dir validation & installation ─────────────────────────

    /// <summary>Validates a Witcher 3 game directory. Returns error message or null when valid.</summary>
    public static string? ValidateGameDir(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return "目录不存在";
        string exe = Path.Combine(gameDir, "bin", "x64", "witcher3.exe");
        if (!File.Exists(exe) && !File.Exists(Path.Combine(gameDir, "bin", "x64_dx12", "witcher3.exe")))
            return "未找到 bin\\x64 或 bin\\x64_dx12 下的 witcher3.exe，请选择游戏根目录。";
        string content0 = Path.Combine(gameDir, "content", "content0");
        if (!Directory.Exists(content0) || !File.Exists(Path.Combine(content0, "bundles", "r4gui.bundle")))
            return "未找到 content\\content0\\bundles\\r4gui.bundle，游戏安装可能不完整。";
        return null;
    }

}
