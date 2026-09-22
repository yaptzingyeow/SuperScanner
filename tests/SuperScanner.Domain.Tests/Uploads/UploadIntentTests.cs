using SuperScanner.Domain.Uploads;

namespace SuperScanner.Domain.Tests.Uploads;

public sealed class UploadIntentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedIntent_CannotReopenAndOverwriteAcceptedMetadata()
    {
        var intent = CreatePendingIntent();
        var acceptedAt = Now.AddMinutes(1);
        intent.Accept("imports/first", acceptedAt);

        Assert.Throws<InvalidOperationException>(() => intent.TryMarkPendingValidation(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => intent.Accept("imports/second", Now.AddMinutes(3)));
        Assert.Equal(UploadIntentState.Accepted, intent.State);
        Assert.Equal("imports/first", intent.AcceptedObjectKey);
        Assert.Equal(acceptedAt, intent.AcceptedAt);
    }

    [Fact]
    public void RejectedIntent_CannotReopen()
    {
        var intent = CreatePendingIntent();
        intent.Reject("malware_detected");

        Assert.Throws<InvalidOperationException>(() => intent.TryMarkPendingValidation(Now.AddMinutes(2)));
        Assert.Equal(UploadIntentState.Rejected, intent.State);
        Assert.Equal("malware_detected", intent.ValidationErrorCode);
    }

    [Fact]
    public void ExpansionProgress_RejectsUnacceptedAndInvalidCounts()
    {
        var unaccepted = UploadIntent.Create(Guid.NewGuid(), "owner", Guid.NewGuid(), "quarantine/key", "scan.pdf",
            "application/pdf", 1, new string('a', 64), Now.AddMinutes(5));
        Assert.Throws<InvalidOperationException>(() => unaccepted.BeginExpansion(1));
        Assert.Throws<InvalidOperationException>(() => unaccepted.RecordExpansion(0, 0, null));

        var accepted = CreateAcceptedIntent();
        Assert.Throws<ArgumentOutOfRangeException>(() => accepted.BeginExpansion(-1));
        accepted.BeginExpansion(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => accepted.RecordExpansion(-1, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => accepted.RecordExpansion(0, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => accepted.RecordExpansion(2, 1, "render_failed"));
        Assert.Throws<ArgumentException>(() => accepted.RecordExpansion(1, 1, " "));
    }

    [Fact]
    public void ExpansionProgress_RecordsAndResetsRetryCounts()
    {
        var intent = CreateAcceptedIntent();
        intent.BeginExpansion(3);
        intent.RecordExpansion(2, 1, "pdf_render_failed");

        Assert.Equal(3, intent.DiscoveredPageCount);
        Assert.Equal(2, intent.CreatedPageCount);
        Assert.Equal(1, intent.FailedPageCount);
        Assert.Equal("pdf_render_failed", intent.ExpansionErrorCode);

        intent.BeginExpansion(3);
        intent.RecordExpansion(3, 0, null);
        Assert.Equal(3, intent.CreatedPageCount);
        Assert.Equal(0, intent.FailedPageCount);
        Assert.Null(intent.ExpansionErrorCode);
    }

    private static UploadIntent CreatePendingIntent()
    {
        var intent = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            Guid.NewGuid(),
            "quarantine/document/upload",
            "scan.pdf",
            "application/pdf",
            1200,
            new string('a', 64),
            Now.AddMinutes(5));
        intent.TryMarkPendingValidation(Now);
        return intent;
    }

    private static UploadIntent CreateAcceptedIntent()
    {
        var intent = CreatePendingIntent();
        intent.Accept("imports/document/upload", Now.AddMinutes(1));
        return intent;
    }
}
