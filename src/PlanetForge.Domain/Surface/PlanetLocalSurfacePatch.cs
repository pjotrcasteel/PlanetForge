namespace PlanetForge.Domain.Surface;

public sealed class PlanetLocalSurfacePatch
{
    private readonly IReadOnlyList<PlanetLocalSurfacePoint> points;

    public PlanetLocalSurfacePatch(PlanetLocalFrame frame, double sizeMeters, int cellsPerAxis, IReadOnlyList<PlanetLocalSurfacePoint> points)
    {
        if (!double.IsFinite(sizeMeters) || sizeMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeMeters), sizeMeters, "Patch size must be finite and greater than zero.");
        }

        if (cellsPerAxis < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerAxis), cellsPerAxis, "Cells per axis must be greater than zero.");
        }

        var expectedPointCount = checked((cellsPerAxis + 1) * (cellsPerAxis + 1));
        if (points.Count != expectedPointCount)
        {
            throw new ArgumentException($"Expected {expectedPointCount} points for a {cellsPerAxis}x{cellsPerAxis} patch.", nameof(points));
        }

        Frame = frame;
        SizeMeters = sizeMeters;
        CellsPerAxis = cellsPerAxis;
        this.points = points;
    }

    public PlanetLocalFrame Frame { get; }

    public double SizeMeters { get; }

    public int CellsPerAxis { get; }

    public int PointsPerAxis => CellsPerAxis + 1;

    public IReadOnlyList<PlanetLocalSurfacePoint> Points => points;

    public PlanetLocalSurfacePoint GetPoint(int x, int y)
    {
        if (x < 0 || x >= PointsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "X must be inside the patch point grid.");
        }

        if (y < 0 || y >= PointsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Y must be inside the patch point grid.");
        }

        return points[(y * PointsPerAxis) + x];
    }
}
