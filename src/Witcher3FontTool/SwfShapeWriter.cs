using System.Numerics;

namespace Witcher3FontTool;

/// <summary>MSB-first bit writer (SWF bit packing).</summary>
public sealed class BitWriter
{
    private readonly MemoryStream _ms = new();
    private int _bitCount;
    private byte _cur;

    public void WriteBit(int bit)
    {
        _cur |= (byte)((bit & 1) << (7 - _bitCount));
        if (++_bitCount == 8) { _ms.WriteByte(_cur); _cur = 0; _bitCount = 0; }
    }

    public void WriteUBits(long value, int bits)
    {
        for (int i = bits - 1; i >= 0; i--) WriteBit((int)((value >> i) & 1));
    }

    public void WriteSBits(long value, int bits)
    {
        if (value < 0) value &= (1L << bits) - 1;
        WriteUBits(value, bits);
    }

    /// <summary>Signed value with UB[5] bit-count prefix, minimal bits.</summary>
    public void WriteBitsFit(long value)
    {
        int bits = value switch
        {
            0 => 0,
            _ => BitOperations.Log2((ulong)Math.Abs(value)) + 2,
        };
        WriteUBits((uint)bits, 5);
        WriteSBits(value, bits);
    }

    /// <summary>SWF MoveData: ONE shared UB[5] count, then both deltas with that width.</summary>
    public void WriteMoveData(long x, long y)
    {
        int bits = MinBitsSignedPair(x, y);
        WriteUBits((uint)bits, 5);
        WriteSBits(x, bits);
        WriteSBits(y, bits);
    }

    private static int MinBitsSignedPair(long a, long b)
    {
        int bits = 1;
        long lo = Math.Min(a, b), hi = Math.Max(a, b);
        while (bits < 31 && !(lo >= -(1L << (bits - 1)) && hi <= (1L << (bits - 1)) - 1)) bits++;
        return bits;
    }

    public void AlignByte()
    {
        while (_bitCount != 0) WriteBit(0);
    }

    public long BitLength => _ms.Length * 8L + _bitCount;

    public byte[] ToArray()
    {
        var bytes = _ms.ToArray();
        if (_bitCount > 0)
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = _cur;
        }
        return bytes;
    }
}

/// <summary>Builds a DefineFont3 tag from TrueType outlines.</summary>
public static class DefineFont3Builder
{
    public const int TagCode = 75;

    public sealed class Options
    {
        public ushort FontId { get; init; } = 1;
        public string FontName { get; init; } = "文鼎UD晶熙黑体G30_D";
        public byte LanguageCode { get; init; } = 4; // Simplified Chinese
        public double TwipsPerUnit { get; init; } = 10;
    }

    public static double PickScale(IOutlineFont font)
    {
        return 20480.0 / font.UnitsPerEm;
    }

    public readonly record struct BuildStats(int Glyphs, int TagLength, short AscentTwips, short DescentTwips);

    public static byte[] Build(IOutlineFont font, Options opt, IReadOnlyList<uint> codePoints, out BuildStats stats)
    {
        if (Math.Ceiling(font.UnitsPerEm * opt.TwipsPerUnit) > short.MaxValue)
            throw new InvalidOperationException("TwipsPerUnit too large for int16 fields");

        double s = opt.TwipsPerUnit;
        int n = codePoints.Count;
        if (n > ushort.MaxValue) throw new InvalidOperationException("too many glyphs for DefineFont3 (u16)");

        var gids = codePoints.Select(font.GlyphIndex).ToArray();
        var shapes = new byte[n][];
        var advances = new short[n];

        for (int i = 0; i < n; i++)
        {
            ushort gid = gids[i];
            advances[i] = (short)Math.Clamp(Math.Round(font.AdvanceOf(gid) * s), short.MinValue, short.MaxValue);
            var outline = font.GetGlyphOutline(gid);
            shapes[i] = outline is { Contours.Length: > 0 }
                ? EncodeGlyphShape(outline, s)
                : EmptyShape();
        }

        var body = new MemoryStream();
        var bw = new BinaryWriter(body);
        bw.Write(opt.FontId);
        byte flags = 0x80 /* hasLayout */ | 0x08 /* wideOffsets */ | 0x04 /* wideCodes */;
        bw.Write(flags);
        bw.Write(opt.LanguageCode);
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(opt.FontName);
        if (nameBytes.Length > 255) throw new InvalidOperationException("font name too long");
        bw.Write((byte)nameBytes.Length);
        bw.Write(nameBytes);
        bw.Write((ushort)n);

        // offset table: offsets relative to start of offset table
        int offsetTableBytes = n * 4 + 4;
        long cursor = offsetTableBytes;
        var offs = new uint[n];
        for (int i = 0; i < n; i++)
        {
            offs[i] = (uint)cursor;
            cursor += shapes[i].Length;
        }
        uint codeTableOffset = (uint)cursor;
        for (int i = 0; i < n; i++) bw.Write(offs[i]);
        bw.Write(codeTableOffset);
        for (int i = 0; i < n; i++) bw.Write(shapes[i]);
        for (int i = 0; i < n; i++) bw.Write((ushort)codePoints[i]);

        // layout section
        bw.Write(Metric(font.Ascender * s));
        bw.Write(Metric(-font.Descender * s));
        bw.Write(Metric(font.LineGap * s));
        for (int i = 0; i < n; i++) bw.Write(advances[i]);
        for (int i = 0; i < n; i++)
        {
            var o = font.GetGlyphOutline(gids[i]);
            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            if (o is { Contours.Length: > 0 })
            {
                x0 = (int)Math.Round(o.XMin * s);
                y0 = (int)Math.Round(-o.YMax * s);
                x1 = (int)Math.Round(o.XMax * s);
                y1 = (int)Math.Round(-o.YMin * s);
            }
            WriteRect(body, x0, x1, y0, y1);
        }
        bw.Write((ushort)0); // kerning count

        stats = new BuildStats(n, (int)body.Length,
            Metric(font.Ascender * s),
            Metric(-font.Descender * s));
        return body.ToArray();
    }

    // ── shape encoding ──
    public static bool LogShape = false;

    private static byte[] EncodeGlyphShape(GlyphOutline outline, double s)
    {
        var bw = new BitWriter();
        bw.WriteUBits(1, 4);  // NumFillBits = 1
        bw.WriteUBits(0, 4);  // NumLineBits = 0
        if (LogShape) Console.WriteLine($"    [W] begin contours={outline.Contours.Length}");

        foreach (var (moveX, moveY, edges) in ContourToEdges(outline.Contours, s))
        {
            // SWF StyleChangeRecord: MoveTo fields precede the fill index.
            bw.WriteUBits(3, 6); // Type=0, FillStyle0=1, MoveTo=1
            bw.WriteMoveData(moveX, moveY);
            bw.WriteBit(1); // FillStyle0 index (NumFillBits=1)
            if (LogShape) Console.WriteLine($"    [W] style move=({moveX},{moveY}) edges={edges.Count} bitPos={bw.BitLength}");

            int penX = moveX, penY = moveY;
            foreach (var e in edges)
            {
                if (e.Kind == EdgeKind.Line)
                {
                    int dx = e.X2 - penX, dy = e.Y2 - penY;
                    int numBits = MinBitsSigned(Math.Max(Math.Abs(dx), Math.Abs(dy)));
                    bw.WriteBit(1);
                    bw.WriteBit(1); // StraightFlag
                    if (numBits > 17) throw new InvalidDataException("字形边超出 SWF 范围。");
                    bw.WriteUBits((uint)(numBits - 2), 4);
                    bw.WriteBit(1); // GeneralLineFlag
                    bw.WriteSBits(dx, numBits);
                    bw.WriteSBits(dy, numBits);
                    penX = e.X2; penY = e.Y2;
                    if (LogShape) Console.WriteLine($"    [W] line d=({dx},{dy}) nb={numBits} bitPos={bw.BitLength}");
                }
                else
                {
                    int cdx = e.Cx - penX, cdy = e.Cy - penY;
                    int adx = e.X2 - e.Cx, ady = e.Y2 - e.Cy;
                    int numBits = MinBitsSigned(Math.Max(Math.Max(Math.Abs(cdx), Math.Abs(cdy)), Math.Max(Math.Abs(adx), Math.Abs(ady))));
                    bw.WriteBit(1);
                    bw.WriteBit(0); // curved
                    if (numBits > 17) throw new InvalidDataException("字形边超出 SWF 范围。");
                    bw.WriteUBits((uint)(numBits - 2), 4);
                    bw.WriteSBits(cdx, numBits);
                    bw.WriteSBits(cdy, numBits);
                    bw.WriteSBits(adx, numBits);
                    bw.WriteSBits(ady, numBits);
                    penX = e.X2; penY = e.Y2;
                    if (LogShape) Console.WriteLine($"    [W] quad d=({cdx},{cdy}|{adx},{ady}) nb={numBits} bitPos={bw.BitLength}");
                }
            }
        }

        // end of shape
        bw.WriteBit(0);
        bw.WriteUBits(0, 5);
        return bw.ToArray();
    }

    private enum EdgeKind { Line, Quad }

    private readonly record struct Edge(EdgeKind Kind, int Cx, int Cy, int X2, int Y2);

    /// <summary>
    /// Converts TTF contours (quadratic on/off-curve rings) into per-contour SWF edges.
    /// Returns pen start (move target) and absolute edge list per contour.
    /// </summary>
    private static List<(int MoveX, int MoveY, List<Edge> Edges)> ContourToEdges(Contour[] contours, double s)
    {
        var result = new List<(int, int, List<Edge>)>();
        foreach (var contour in contours)
        {
            var pts = contour.Points;
            int n = pts.Length;
            if (n == 0) continue;

            // scale to twips
            var xy = new (int x, int y)[n];
            var on = new bool[n];
            for (int i = 0; i < n; i++)
            {
                xy[i] = ((int)Math.Round(pts[i].X * s), (int)Math.Round(-pts[i].Y * s));
                on[i] = pts[i].OnCurve;
            }

            // rotate so index 0 is on-curve; if none, start at midpoint of last/first
            (int x, int y)[] ring;
            bool[] ringOn;
            int firstOn = Array.IndexOf(on, true);
            if (firstOn < 0)
            {
                ring = new (int, int)[n + 1];
                ringOn = new bool[n + 1];
                ring[0] = ((xy[n - 1].x + xy[0].x) / 2, (xy[n - 1].y + xy[0].y) / 2);
                ringOn[0] = true;
                for (int i = 0; i < n; i++) { ring[i + 1] = xy[i]; ringOn[i + 1] = false; }
            }
            else
            {
                ring = new (int, int)[n];
                ringOn = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    ring[i] = xy[(firstOn + i) % n];
                    ringOn[i] = on[(firstOn + i) % n];
                }
            }

            int m = ring.Length;
            var edges = new List<Edge>();
            int penX = ring[0].x, penY = ring[0].y;

            int i2 = 0;
            while (i2 < m)
            {
                // ringOn[i2] is true by construction
                int j = i2 + 1;
                var ctrls = new List<(int x, int y)>();
                while (j < m && !ringOn[j]) { ctrls.Add(ring[j]); j++; }

                bool wrap = j >= m; // closes back to ring[0]
                var nextOn = wrap ? ring[0] : ring[j];

                EmitSegment(edges, ref penX, ref penY, ring[i2], ctrls, nextOn);
                if (wrap) break;
                i2 = j;
            }

            result.Add((ring[0].x, ring[0].y, edges));
        }
        return result;
    }

    private static void EmitSegment(List<Edge> edges, ref int penX, ref int penY,
        (int x, int y) anchor, List<(int x, int y)> ctrls, (int x, int y) nextOn)
    {
        switch (ctrls.Count)
        {
            case 0:
                edges.Add(new Edge(EdgeKind.Line, 0, 0, nextOn.x, nextOn.y));
                penX = nextOn.x; penY = nextOn.y;
                break;
            default:
            {
                for (int k = 0; k < ctrls.Count; k++)
                {
                    bool last = k == ctrls.Count - 1;
                    var ctrl = ctrls[k];
                    (int x, int y) target = last
                        ? nextOn
                        : ((ctrls[k].x + ctrls[k + 1].x) / 2, (ctrls[k].y + ctrls[k + 1].y) / 2);
                    edges.Add(new Edge(EdgeKind.Quad, ctrl.x, ctrl.y, target.x, target.y));
                    penX = target.x; penY = target.y;
                }
                break;
            }
        }
    }

    private static short Metric(double v) => checked((short)Math.Round(v));

    private static int MinBitsSigned(int v)
    {
        int bits = 2;
        while (bits < 31 && !(v >= -(1L << (bits - 1)) && v <= (1L << (bits - 1)) - 1)) bits++;
        return Math.Max(bits, 2);
    }

    private static byte[] EmptyShape()
    {
        return new byte[] { 0x10, 0x0A, 0x00 }; // FillStyle0=1, then EndShape
    }

    private static void WriteRect(MemoryStream ms, int x0, int x1, int y0, int y1)
    {
        var bw = new BitWriter();
        int nBits = 1;
        foreach (var v in new[] { x0, x1, y0, y1 })
        {
            int b = v == 0 ? 1 : BitOperations.Log2((ulong)Math.Abs(v)) + 2;
            nBits = Math.Max(nBits, b);
        }
        bw.WriteUBits(nBits, 5);
        bw.WriteSBits(x0, nBits); bw.WriteSBits(x1, nBits);
        bw.WriteSBits(y0, nBits); bw.WriteSBits(y1, nBits);
        ms.Write(bw.ToArray());
    }
}
