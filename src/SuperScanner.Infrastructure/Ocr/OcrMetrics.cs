using System.Diagnostics.Metrics;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class OcrMetrics
{
    public const string MeterName = "SuperScanner.Ocr";
    private static readonly Meter Meter = new(MeterName);
    private readonly Counter<long> jobs = Meter.CreateCounter<long>("superscanner.ocr.jobs");
    private readonly Histogram<double> queueMilliseconds = Meter.CreateHistogram<double>("superscanner.ocr.queue.duration", "ms");
    private readonly Histogram<double> providerMilliseconds = Meter.CreateHistogram<double>("superscanner.ocr.provider.duration", "ms");
    private readonly Histogram<long> elements = Meter.CreateHistogram<long>("superscanner.ocr.elements");
    private readonly Histogram<double> confidence = Meter.CreateHistogram<double>("superscanner.ocr.confidence");

    public void Started(double queuedMilliseconds, int retryCount) =>
        queueMilliseconds.Record(queuedMilliseconds,
            new KeyValuePair<string, object?>("outcome", "started"));

    public void Completed(string provider, double elapsedMs, int elementCount,
        double? aggregateConfidence, bool stale)
    {
        jobs.Add(1, new("provider", provider), new("outcome", "ready"), new("stale", stale));
        providerMilliseconds.Record(elapsedMs,
            new KeyValuePair<string, object?>("provider", provider));
        elements.Record(elementCount,
            new KeyValuePair<string, object?>("provider", provider));
        if (aggregateConfidence is double value)
            confidence.Record(value,
                new KeyValuePair<string, object?>("provider", provider));
    }

    public void Failed(string failureCode, bool retryable) =>
        jobs.Add(1, new("outcome", retryable ? "retry" : "failed"), new("failure_code", failureCode));
}
