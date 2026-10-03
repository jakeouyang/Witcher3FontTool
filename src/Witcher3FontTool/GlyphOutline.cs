namespace Witcher3FontTool;

public readonly record struct GlyphPoint(int X, int Y, bool OnCurve);

public sealed class Contour
{
    public GlyphPoint[] Points { get; }
    public Contour(GlyphPoint[] points) => Points = points;
}

public sealed class GlyphOutline
{
    public int XMin { get; }
    public int YMin { get; }
    public int XMax { get; }
    public int YMax { get; }
    public Contour[] Contours { get; }
    public GlyphOutline(int xMin, int yMin, int xMax, int yMax, Contour[] contours)
    {
        XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax; Contours = contours;
    }
}
