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
}
