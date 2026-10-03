using System.Text;

namespace Witcher3FontTool;

/// <summary>Decodes DefineFont3 glyph shapes (bit-level) — used to self-verify generated files.</summary>
public static class SwfShapeDecoder
{
    public sealed class DecodedShape
    {
        public List<(int x, int y)> Moves { get; } = new();
        public List<(int x1, int y1, int cx, int cy, int x2, int y2)> Quads { get; } = new();
        public List<(int x1, int y1, int x2, int y2)> Lines { get; } = new();
        public (short x0, short y0, short x1, short y1) Bounds;
        public short Advance;

        /// <summary>Contours reconstructed in path order: each is a closed polyline of on-curve points.</summary>
        public List<List<(double x, double y)>> Contours { get; } = new();
    }

    /// <summary>Reads a DefineFont3 tag and decodes glyphs whose codes are listed.</summary>
    public static Dictionary<uint, DecodedShape> Decode(byte[] font3Tag, IEnumerable<uint> wantedCodes)
    {
        var wanted = new HashSet<uint>(wantedCodes);
        var result = new Dictionary<uint, DecodedShape>();

        ushort numGlyphs = BitConverter.ToUInt16(font3Tag, 5 + font3Tag[4]);
        int offsetTableStart = 7 + font3Tag[4];

        long glyphDataStart = offsetTableStart; // glyph offsets are relative to the offset table start
        uint codeTableOffset = BitConverter.ToUInt32(font3Tag, (int)(offsetTableStart + numGlyphs * 4L));
        long codeTableStart = offsetTableStart + codeTableOffset;

        for (int i = 0; i < numGlyphs; i++)
        {
            uint code = BitConverter.ToUInt16(font3Tag, (int)(codeTableStart + i * 2L));
            if (!wanted.Contains(code)) continue;
            uint off = BitConverter.ToUInt32(font3Tag, offsetTableStart + i * 4);
            uint next = i + 1 < numGlyphs ? BitConverter.ToUInt32(font3Tag, offsetTableStart + (i + 1) * 4) : codeTableOffset;
            int len = (int)(next - off);
            var shape = DecodeShape(font3Tag, (int)(glyphDataStart + off), len);
            result[code] = shape;        }

        // layout: ascent/descent/leading + advances after code table
        long layoutStart = codeTableStart + numGlyphs * 2L;
        return result;
    }

    public static bool Trace = false;

    public static DecodedShape DecodeShape(byte[] data, int start, int len)
    {
        bool trace = Trace;
        var s = new DecodedShape();
        int bytePos = start;
        int bitPos = 0;

        int U(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
            {
                if (bytePos >= start + len) throw new InvalidDataException("字形位流越界");
                if (bitPos == 0) { bitPos = 8; }
                bitPos--;
                v = v << 1 | (data[bytePos] >> bitPos & 1);
                if (bitPos == 0) bytePos++;
            }
            return v;
        }
        int S(int n)
        {
            int v = U(n);
            if (n > 0 && (v & (1 << (n - 1))) != 0) v -= 1 << n;
            return v;
        }

        int numFillBits = U(4), numLineBits = U(4);
        int penX = 0, penY = 0;
        int rec = 0;
        List<(double x, double y)>? cur = null;
        if (trace) Console.WriteLine($"    DecodeShape start={start} len={len} numFillBits={numFillBits} numLineBits={numLineBits} bitPosNow={bytePos * 8 - bitPos}");
        while (true)
        {
            rec++;
            int recBit = bytePos * 8 - bitPos;
            int type = U(1);
            if (type == 1)
            {
                int straight = U(1);
                int numBits = U(4) + 2;
                if (straight == 1)
                {
                    int general = U(1);
                    int dx, dy;
                    if (general == 1) { dx = S(numBits); dy = S(numBits); }
                    else
                    {
                        int vert = U(1);
                        dx = vert == 1 ? 0 : S(numBits);
                        dy = vert == 1 ? S(numBits) : 0;
                    }
                    int x1 = penX, y1 = penY;
                    penX += dx; penY += dy;
                    s.Lines.Add((x1, y1, penX, penY));
                    cur?.Add((penX, penY));
                }
                else
                {
                    int cdx = S(numBits), cdy = S(numBits);
                    int adx = S(numBits), ady = S(numBits);
                    int cx = penX + cdx, cy = penY + cdy, ax = cx + adx, ay = cy + ady;
                    s.Quads.Add((penX, penY, cx, cy, ax, ay));
                    if (cur != null)
                        for (int t = 1; t <= 8; t++)
                        {
                            double f = t / 8.0, g = 1 - f;
                            cur.Add((g * g * penX + 2 * g * f * cx + f * f * ax,
                                     g * g * penY + 2 * g * f * cy + f * f * ay));
                        }
                    penX = ax; penY = ay;
                }
            }
            else
            {
                int flags = U(5);
                if (flags == 0) { if (trace) Console.WriteLine($"    [rec{rec} @bit{recBit}] END @byte{bytePos}"); break; }
                if ((flags & 16) != 0) throw new InvalidDataException("Font shape cannot define new styles");
                if ((flags & 1) != 0)
                {
                    int moveBits = U(5);
                    penX = S(moveBits); penY = S(moveBits);
                    s.Moves.Add((penX, penY));
                    cur = new List<(double x, double y)> { (penX, penY) };
                    s.Contours.Add(cur);
                }
                if ((flags & 2) != 0 && U(numFillBits) != 1) throw new InvalidDataException("Invalid font fill index");
                if ((flags & 4) != 0) U(numFillBits);
                if ((flags & 8) != 0) U(numLineBits);
            }
        }
        while (bytePos < start + len)
            if (U(1) != 0) throw new InvalidDataException("Nonzero trailing shape bits");
        return s;
    }
}

/// <summary>Scanline even-odd fill rasterizer for ASCII preview of decoded glyphs.</summary>
public static class GlyphRasterizer
{
    public static string RenderAscii(SwfShapeDecoder.DecodedShape shape, int width = 56)
    {
        if (shape.Contours.Count == 0) return "(empty)";

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var c in shape.Contours)
            foreach (var (x, y) in c)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        double w = maxX - minX, h = maxY - minY;
        if (w <= 0 || h <= 0) return "(degenerate)";

        int rows = Math.Max(6, (int)Math.Round(h / w * width / 2.1));
        double sx = (width - 1) / w, sy = (rows - 1) / h;

        var grid = new char[rows, width];
        for (int r = 0; r < rows; r++)
            for (int c2 = 0; c2 < width; c2++)
                grid[r, c2] = ' ';

        // scanline even-odd fill (y grows downward in SWF, so flip)
        for (int r = 0; r < rows; r++)
        {
            double py = minY + r * (h / Math.Max(1, rows - 1));
            var xs = new List<double>();
            foreach (var contour in shape.Contours)
            {
                int n = contour.Count;
                for (int i = 0; i < n; i++)
                {
                    var (x1, y1) = contour[i];
                    var (x2, y2) = contour[(i + 1) % n];
                    if ((y1 <= py && y2 > py) || (y2 <= py && y1 > py))
                        xs.Add(x1 + (py - y1) / (y2 - y1) * (x2 - x1));
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int cA = (int)Math.Round((xs[k] - minX) * sx);
                int cB = (int)Math.Round((xs[k + 1] - minX) * sx);
                for (int c2 = Math.Max(0, cA); c2 <= Math.Min(width - 1, cB); c2++)
                    grid[r, c2] = '#';
            }
        }

        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c2 = 0; c2 < width; c2++) sb.Append(grid[r, c2]);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}