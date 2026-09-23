namespace SuperScanner.Domain.TextEditing;

public sealed record NormalizedBox
{
    public NormalizedBox(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || x < 0 || x > 1)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y) || y < 0 || y > 1)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (!double.IsFinite(width) || width <= 0 || width > 1 || x + width > 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0 || height > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(height));

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}
