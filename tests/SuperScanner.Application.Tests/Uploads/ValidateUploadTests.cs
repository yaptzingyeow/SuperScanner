using System.Security.Cryptography;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Tests.Uploads;

public sealed class ValidateUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 1, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfBytes = "%PDF-1.7\nvalid"u8.ToArray();

    public static TheoryData<byte[], string> SupportedSignatures => new()
    {
        { "%PDF-"u8.ToArray(), "application/pdf" },
        { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png" },
        { new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg" },
        { new byte[] { 0, 0, 0, 0, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 }, "image/heic" },
        { new byte[] { 0, 0, 0, 0, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x78 }, "image/heic" }
    };

    [Theory]
    [MemberData(nameof(SupportedSignatures))]
    public void DetectMediaType_RecognizesSupportedSignatures(byte[] header, string expectedMediaType)
    {
        Assert.Equal(expectedMediaType, UploadFileSignature.DetectMediaType(header));
    }

    [Fact]
    public void DetectMediaType_RejectsUnknownSignature()
    {
        Assert.Throws<InvalidDataException>(() => UploadFileSignature.DetectMediaType("unknown"u8));
    }

    [Fact]
    public async Task CleanUpload_IsPromotedToContentAddressedOriginal()
    {
        var fixture = CreateFixture(PdfBytes);

        var result = await fixture.Handler.ValidateAsync(
            fixture.Upload.Id,
            new FixedMalwareScanner(MalwareScanResult.Clean()),
            CancellationToken.None);

        var expectedKey = $"originals/{fixture.Document.Id:N}/{fixture.Page.Id:N}/{Sha256(PdfBytes)}";
        Assert.Equal(UploadValidationOutcome.Accepted, result.Outcome);
        Assert.Equal(UploadIntentState.Accepted, fixture.Upload.State);
        Assert.Equal(expectedKey, fixture.Page.OriginalObjectKey);
        Assert.Equal((fixture.Upload.QuarantineObjectKey, expectedKey), Assert.Single(fixture.Store.Promotions));
    }

    [Fact]
    public async Task InfectedUpload_IsDeletedAndNeverAccepted()
    {
        var fixture = CreateFixture(PdfBytes);

        var result = await fixture.Handler.ValidateAsync(
            fixture.Upload.Id,
            new FixedMalwareScanner(MalwareScanResult.Infected("Eicar-Test-Signature")),
            CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("malware_detected", result.ErrorCode);
        Assert.Equal(UploadIntentState.Rejected, fixture.Upload.State);
        Assert.Null(fixture.Page.OriginalObjectKey);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
        Assert.Empty(fixture.Store.Promotions);
    }

    [Fact]
    public async Task SignatureMismatch_IsRejectedBeforeMalwareScanning()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 };
        var fixture = CreateFixture(pngBytes, declaredMediaType: "application/pdf");
        var scanner = new CountingMalwareScanner();

        var result = await fixture.Handler.ValidateAsync(fixture.Upload.Id, scanner, CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("signature_mismatch", result.ErrorCode);
        Assert.Equal(0, scanner.ScanCalls);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
        Assert.Null(fixture.Page.OriginalObjectKey);
    }

    [Fact]
    public async Task HashMismatch_IsRejectedBeforeMalwareScanning()
    {
        var fixture = CreateFixture(PdfBytes, declaredSha256: new string('0', 64));
        var scanner = new CountingMalwareScanner();

        var result = await fixture.Handler.ValidateAsync(fixture.Upload.Id, scanner, CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("hash_mismatch", result.ErrorCode);
        Assert.Equal(0, scanner.ScanCalls);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
    }

    [Fact]
    public async Task StreamSizeMismatch_IsRejectedBeforeMalwareScanning()
    {
        var fixture = CreateFixture(PdfBytes, declaredSizeBytes: PdfBytes.LongLength + 1);
        var scanner = new CountingMalwareScanner();

        var result = await fixture.Handler.ValidateAsync(fixture.Upload.Id, scanner, CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("size_mismatch", result.ErrorCode);
        Assert.Equal(0, scanner.ScanCalls);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
    }

    [Fact]
    public async Task OversizedStream_IsRejectedAtConfiguredLimit()
    {
        var bytes = "%PDF-"u8.ToArray().Concat(new byte[20]).ToArray();
        var fixture = CreateFixture(bytes, maxSizeBytes: 16);

        var result = await fixture.Handler.ValidateAsync(
            fixture.Upload.Id,
            new CountingMalwareScanner(),
            CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("size_limit_exceeded", result.ErrorCode);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
    }

    [Fact]
    public async Task ExpiredUpload_IsRejectedWithoutOpeningQuarantineObject()
    {
        var fixture = CreateFixture(PdfBytes, expiresAt: Now);

        var result = await fixture.Handler.ValidateAsync(
            fixture.Upload.Id,
            new CountingMalwareScanner(),
            CancellationToken.None);

        Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
        Assert.Equal("upload_expired", result.ErrorCode);
        Assert.Equal(0, fixture.Store.OpenReadCalls);
        Assert.Equal(fixture.Upload.QuarantineObjectKey, Assert.Single(fixture.Store.DeletedKeys));
    }

    [Fact]
    public async Task ScannerOutage_RemainsQuarantinedForRetry()
    {
        var fixture = CreateFixture(PdfBytes);

        await Assert.ThrowsAsync<MalwareScannerUnavailableException>(() => fixture.Handler.ValidateAsync(
            fixture.Upload.Id,
            new UnavailableMalwareScanner(),
            CancellationToken.None));

        Assert.Equal(UploadIntentState.PendingValidation, fixture.Upload.State);
        Assert.Null(fixture.Page.OriginalObjectKey);
        Assert.Empty(fixture.Store.DeletedKeys);
        Assert.Empty(fixture.Store.Promotions);
    }

    private static ValidationFixture CreateFixture(
        byte[] bytes,
        string declaredMediaType = "application/pdf",
        string? declaredSha256 = null,
        long? declaredSizeBytes = null,
        long maxSizeBytes = 1024,
        DateTimeOffset? expiresAt = null)
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Private scan", Now);
        var page = document.AddPage(Guid.NewGuid(), 50, Now);
        var upload = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            document.Id,
            page.Id,
            $"quarantine/{document.Id:N}/{Guid.NewGuid():N}",
            declaredMediaType,
            declaredSizeBytes ?? bytes.LongLength,
            declaredSha256 ?? Sha256(bytes),
            expiresAt ?? Now.AddMinutes(5));
        upload.TryMarkPendingValidation(Now.AddSeconds(-1));

        var repository = new InMemoryUploadValidationRepository(upload, page);
        var store = new RecordingObjectStore(bytes);
        var handler = new ValidateUpload(
            repository,
            store,
            new FixedClock(Now),
            new UploadValidationPolicy(maxSizeBytes));
        return new ValidationFixture(document, page, upload, store, handler);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ValidationFixture(
        Document Document,
        Page Page,
        UploadIntent Upload,
        RecordingObjectStore Store,
        ValidateUpload Handler);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryUploadValidationRepository(UploadIntent upload, Page page)
        : IUploadValidationRepository
    {
        public Task<UploadValidationTarget?> FindAsync(Guid uploadId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadValidationTarget?>(
                upload.Id == uploadId ? new UploadValidationTarget(upload, page) : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingObjectStore(byte[] bytes) : IObjectStore
    {
        public int OpenReadCalls { get; private set; }
        public List<string> DeletedKeys { get; } = [];
        public List<(string QuarantineKey, string AcceptedKey)> Promotions { get; } = [];

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
        {
            OpenReadCalls++;
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken)
        {
            Promotions.Add((quarantineKey, acceptedKey));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            DeletedKeys.Add(objectKey);
            return Task.CompletedTask;
        }

        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedMalwareScanner(MalwareScanResult result) : IMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CountingMalwareScanner : IMalwareScanner
    {
        public int ScanCalls { get; private set; }

        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            ScanCalls++;
            return Task.FromResult(MalwareScanResult.Clean());
        }
    }

    private sealed class UnavailableMalwareScanner : IMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromException<MalwareScanResult>(new MalwareScannerUnavailableException());
    }
}
