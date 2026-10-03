using System.Windows.Media;

namespace Witcher3FontTool;

public interface IOutlineFont
{
    ushort UnitsPerEm { get; }
    short Ascender { get; }
    short Descender { get; }
    short LineGap { get; }
    ushort GlyphIndex(uint code);
    int AdvanceOf(ushort glyph);
    GlyphOutline? GetGlyphOutline(ushort glyph, int depth = 0);
}

// Windows' font engine handles TrueType, collections, CFF and composite outlines.
public sealed class OutlineFont : IOutlineFont
{
    readonly GlyphTypeface face;
    readonly Dictionary<ushort, GlyphOutline> cache = new();
    public ushort UnitsPerEm => 16384;
    public short Ascender => checked((short)Math.Round(face.Baseline * UnitsPerEm));
    public short Descender => checked((short)-Math.Round((face.Height - face.Baseline) * UnitsPerEm));
    public short LineGap => 0;
    public string Family => face.FamilyNames.Values.FirstOrDefault() ?? "Font";
    public OutlineFont(string path)
    {
        face = new GlyphTypeface(new Uri(Path.GetFullPath(path)));
    }
    public ushort GlyphIndex(uint code) => face.CharacterToGlyphMap.TryGetValue((int)code, out var id) ? id : (ushort)0;
    public int AdvanceOf(ushort glyph) => checked((int)Math.Round(face.AdvanceWidths[glyph] * UnitsPerEm));
    public GlyphOutline GetGlyphOutline(ushort glyph, int depth = 0)
    {
        if (cache.TryGetValue(glyph, out var cached)) return cached;
        var geometry = PathGeometry.CreateFromGeometry(face.GetGlyphOutline(glyph, UnitsPerEm, 1));
        var contours = geometry.Figures.Select(ConvertFigure).ToArray();
        var points = contours.SelectMany(c => c.Points).ToArray();
        int Bound(Func<GlyphPoint, int> selector, bool min) => points.Length == 0 ? 0 : (min ? points.Min(selector) : points.Max(selector));
        return cache[glyph] = new GlyphOutline(Bound(p => p.X, true), Bound(p => p.Y, true), Bound(p => p.X, false), Bound(p => p.Y, false), contours);
    }
    static Contour ConvertFigure(PathFigure figure)
    {
        var points = new List<GlyphPoint>();
        var pen = figure.StartPoint;
        void Add(System.Windows.Point p, bool on) => points.Add(new GlyphPoint((int)Math.Round(p.X), (int)Math.Round(-p.Y), on));
        void Quad(System.Windows.Point c, System.Windows.Point end) { Add(c, false); Add(end, true); pen = end; }
        System.Windows.Point Mid(System.Windows.Point a, System.Windows.Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        void Cubic(System.Windows.Point p0, System.Windows.Point p1, System.Windows.Point p2, System.Windows.Point p3, int depth = 0)
        {
            var error = new System.Windows.Vector(p3.X - 3 * p2.X + 3 * p1.X - p0.X, p3.Y - 3 * p2.Y + 3 * p1.Y - p0.Y);
            if (error.Length <= 24 || depth >= 16)
            {
                Quad(new System.Windows.Point((3 * (p1.X + p2.X) - p0.X - p3.X) / 4, (3 * (p1.Y + p2.Y) - p0.Y - p3.Y) / 4), p3);
                return;
            }
            var a = Mid(p0, p1); var b = Mid(p1, p2); var c = Mid(p2, p3);
            var d = Mid(a, b); var e = Mid(b, c); var m = Mid(d, e);
            Cubic(p0, a, d, m, depth + 1); Cubic(m, e, c, p3, depth + 1);
        }
        Add(pen, true);
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment l: Add(l.Point, true); pen = l.Point; break;
                case PolyLineSegment l: foreach (var p in l.Points) { Add(p, true); pen = p; } break;
                case QuadraticBezierSegment q: Quad(q.Point1, q.Point2); break;
                case PolyQuadraticBezierSegment q:
                    for (int i = 0; i < q.Points.Count; i += 2) Quad(q.Points[i], q.Points[i + 1]); break;
                case BezierSegment b: Cubic(pen, b.Point1, b.Point2, b.Point3); break;
                case PolyBezierSegment b:
                    for (int i = 0; i < b.Points.Count; i += 3) Cubic(pen, b.Points[i], b.Points[i + 1], b.Points[i + 2]); break;
                default: throw new InvalidDataException("字体包含不支持的轮廓段");
            }
        }
        return new Contour(points.ToArray());
    }
}
