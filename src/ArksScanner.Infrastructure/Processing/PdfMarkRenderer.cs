using PdfSharp.Drawing;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Infrastructure.Processing;

public static class PdfMarkRenderer
{
    public static void Draw(XGraphics graphics, MarkOverlaySnapshot mark, double pageWidth, double pageHeight)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (mark.MarkId == Guid.Empty || !Enum.IsDefined(mark.Kind) ||
            !double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
            throw new ArgumentException("Invalid PDF mark.", nameof(mark));
        var box = mark.Box ?? throw new ArgumentException("Mark box is required.", nameof(mark));
        _ = new SignatureBox(box.X, box.Y, box.Width, box.Height);
        var style = new PageMarkStyle(mark.Color, mark.StrokeWidth);
        var left = box.X * pageWidth;
        var top = box.Y * pageHeight;
        var width = box.Width * pageWidth;
        var height = box.Height * pageHeight;
        var color = XColor.FromArgb(Convert.ToInt32(style.Color[1..3], 16),
            Convert.ToInt32(style.Color[3..5], 16), Convert.ToInt32(style.Color[5..7], 16));
        var pen = new XPen(color, Math.Min(width, height) * style.StrokeWidth)
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round
        };
        XPoint P(double x, double y) => new(left + width * x / 100, top + height * y / 100);
        if (mark.Kind == PageMarkKind.Check)
            graphics.DrawLines(pen, [P(18, 52), P(42, 76), P(82, 24)]);
        else
        {
            graphics.DrawLine(pen, P(24, 24), P(76, 76));
            graphics.DrawLine(pen, P(76, 24), P(24, 76));
        }
    }
}
