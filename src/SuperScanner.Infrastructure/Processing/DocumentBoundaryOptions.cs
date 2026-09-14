namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentBoundaryOptions
{
    public const string SectionName = "DocumentBoundary";

    public string Mode { get; init; } = "OpenCvOnly";
    public string ModelMetadataPath { get; init; } = "processing/models/document-boundary-model.json";
    public double MaskThreshold { get; init; } = .52;
    public double HighConfidence { get; init; } = .78;
    public double MediumConfidence { get; init; } = .58;
    public int InferenceTimeoutSeconds { get; init; } = 20;
    public int RolloutPercentage { get; init; }

    public bool IsValid() =>
        Mode is "AiPreferred" or "OpenCvOnly" or "ManualOnly"
        && !string.IsNullOrWhiteSpace(ModelMetadataPath)
        && MaskThreshold is > 0 and < 1
        && HighConfidence is >= 0 and <= 1
        && MediumConfidence is >= 0 and <= 1
        && HighConfidence >= MediumConfidence
        && MediumConfidence >= .45
        && InferenceTimeoutSeconds is >= 1 and <= 25
        && RolloutPercentage is >= 0 and <= 100;
}
