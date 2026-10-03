using System.Text;

namespace Witcher3FontTool;

/// <summary>Parses a font library and replaces its registered DefineFont3 slots.</summary>
public sealed class TemplateSwf
{
    public byte[] HeaderBytes { get; }      // rect + framerate + framecount (bytes 8..N of template)
    public List<SwfTag> Tags { get; }
    public int Font3TagCount { get; }

    public sealed record SwfTag(int Code, byte[] Data);

    public TemplateSwf(byte[] swf)
    {
        HeaderBytes = new byte[RectSize(swf, 8) + 4];
        Buffer.BlockCopy(swf, 8, HeaderBytes, 0, HeaderBytes.Length);

        Tags = new List<SwfTag>();
        int pos = 8 + HeaderBytes.Length;
        while (pos + 2 <= swf.Length)
        {
            uint codeAndLen = ReadU16(swf, pos);
            int code = (int)(codeAndLen >> 6);
            uint len = codeAndLen & 0x3F;
            int headerSize = 2;
            if (len == 0x3F) { len = ReadU32(swf, pos + 2); headerSize = 6; }
            if (code == 0) break; // End
            var data = new byte[len];
            Buffer.BlockCopy(swf, pos + headerSize, data, 0, (int)len);
            Tags.Add(new SwfTag(code, data));
            if (code == 75) Font3TagCount++;
            pos = pos + headerSize + (int)len;
        }
    }

    /// <summary>
    /// Rebuilds an uncompressed font library, replacing slots by their original FontID.
    /// Non-font tags retain the game's linkage, family names and export registrations.
    /// </summary>
    public byte[] Rebuild(IReadOnlyDictionary<ushort, byte[]> fonts)
    {
        var tags = new MemoryStream();
        bool replaced = false;
        foreach (var t in Tags)
        {
            byte[] data = t.Data;
            int code = t.Code;
            if (code == 75 && fonts.TryGetValue(BitConverter.ToUInt16(data), out var replacement))
            {
                data = replacement;
                replaced = true;
            }
            WriteTagHeader(tags, code, data.Length);
            tags.Write(data);
        }
        if (!replaced) throw new InvalidOperationException("template has no DefineFont3 tag");
        WriteTagHeader(tags, 0, 0); // End

        uint fileLen = (uint)(8 + HeaderBytes.Length + tags.Length);
        var ms = new MemoryStream();
        ms.WriteByte((byte)'F'); ms.WriteByte((byte)'W'); ms.WriteByte((byte)'S'); ms.WriteByte(15);
        ms.WriteByte((byte)fileLen); ms.WriteByte((byte)(fileLen >> 8)); ms.WriteByte((byte)(fileLen >> 16)); ms.WriteByte((byte)(fileLen >> 24));
        ms.Write(HeaderBytes);
        tags.Position = 0;
        tags.CopyTo(ms);
        return ms.ToArray();
    }

    public static void WriteTagHeader(Stream s, int code, int len)
    {
        if (len < 0x3F)
        {
            ushort v = (ushort)(code << 6 | len);
            s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)(v >> 8));
        }
        else
        {
            ushort v = (ushort)(code << 6 | 0x3F);
            s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)len); s.WriteByte((byte)(len >> 8)); s.WriteByte((byte)(len >> 16)); s.WriteByte((byte)(len >> 24));
        }
    }

    private static int RectSize(byte[] b, int pos) => (5 + (b[pos] >> 3) * 4 + 7) / 8;
    private static uint ReadU16(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8);
    private static uint ReadU32(byte[] b, int p) => b[p] | (uint)b[p + 1] << 8 | (uint)b[p + 2] << 16 | (uint)b[p + 3] << 24;
}
