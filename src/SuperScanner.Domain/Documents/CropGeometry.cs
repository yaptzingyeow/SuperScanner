namespace SuperScanner.Domain.Documents;

public sealed record CropPoint(double X, double Y);

public static class CropGeometry
{
    public static bool IsValid(IReadOnlyList<CropPoint>? points)
    {
        if (points is null || points.Count != 4 || points.Any(p => p is null ||
            !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1)) return false;
        double twiceArea = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = points[i]; var b = points[(i + 1) % 4]; var c = points[(i + 2) % 4];
            if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) <= 0.0001) return false;
            if (Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) < 0.0004) return false;
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return twiceArea >= 0.02 && points[0].Y + points[1].Y < points[2].Y + points[3].Y
            && points[0].X + points[3].X < points[1].X + points[2].X;
    }
}
