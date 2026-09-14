using System.Text.RegularExpressions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Processing;

public sealed record CropDetectionResult(
    CropPoint[] Points,
    double Confidence,
    string Source,
    string? ModelVersion,
    string DiagnosticsCode)
{
    private static readonly Regex SafeCode = new(
        "^[a-z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public bool IsValid()
    {
        if (!CropGeometry.IsValid(Points)
            || !double.IsFinite(Confidence)
            || Confidence is < 0 or > 1
            || Source is not ("Ai" or "OpenCvFallback" or "FullImage")
            || string.IsNullOrWhiteSpace(DiagnosticsCode)
            || DiagnosticsCode.Length > 64
            || !SafeCode.IsMatch(DiagnosticsCode))
        {
            return false;
        }

        if (Source == "Ai")
        {
            return !string.IsNullOrWhiteSpace(ModelVersion) && ModelVersion.Length <= 100;
        }

        return ModelVersion is null && (Source != "FullImage" || Confidence == 0);
    }
}
