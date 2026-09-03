using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Tests.Uploads;

public sealed class CreateUploadIntentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 4, 0, 0, TimeSpan.Zero);
    private readonly Document _document = Document.Create(Guid.NewGuid(), "user-a", "Tax form", Now);

    public static TheoryData<string, long, string> InvalidDeclaredMetadata => new()
    {
        { "text/plain", 1200, new string('a', 64) },
        { "application/pdf", 0, new string('a', 64) },
        { "application/pdf", (25 * 1024 * 1024) + 1, new string('a', 64) },
        { "application/pdf", 1200, new string('A', 64) },
        { "application/pdf", 1200, new string('a', 63) },
        { "application/pdf", 1200, new string('g', 64) }
    };

    [Fact]
    public async Task Create_UsesOpaqueQuarantineKeyAndNeverUserFilename()
    {
        var repository = new InMemoryUploadIntentRepository(_document);
        var store = new RecordingObjectStore();
        var handler = new CreateUploadIntent(
            repository,
            store,
            new FixedClock(Now),
            new UploadPolicy(50, 25 * 1024 * 1024));

        var result = await handler.HandleAsync(
            "user-a",
            _document.Id,
            new CreateUploadRequest(
                "tax-form.pdf",
                "application/pdf",
                1200,
                new string('a', 64)),
            CancellationToken.None);

        Assert.StartsWith($"quarantine/{_document.Id:N}/", store.LastRequest!.ObjectKey, StringComparison.Ordinal);
        Assert.DoesNotContain("tax-form", store.LastRequest.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now.AddMinutes(5), result.ExpiresAt);
        Assert.Equal(result.PageId, Assert.Single(_document.Pages).Id);
    }

    [Theory]
    [MemberData(nameof(InvalidDeclaredMetadata))]
    public async Task Create_RejectsInvalidDeclaredMetadataBeforeReservingPage(
        string mediaType,
        long sizeBytes,
        string sha256Hex)
    {
        var repository = new InMemoryUploadIntentRepository(_document);
        var handler = new CreateUploadIntent(
            repository,
            new RecordingObjectStore(),
            new FixedClock(Now),
            new UploadPolicy(50, 25 * 1024 * 1024));

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            "user-a",
            _document.Id,
            new CreateUploadRequest("scan.bin", mediaType, sizeBytes, sha256Hex),
            CancellationToken.None));

        Assert.Empty(_document.Pages);
        Assert.Empty(repository.Uploads);
    }

    [Fact]
    public async Task Create_DoesNotRevealDocumentOwnedByAnotherUser()
    {
        var repository = new InMemoryUploadIntentRepository(_document);
        var handler = new CreateUploadIntent(
            repository,
            new RecordingObjectStore(),
            new FixedClock(Now),
            new UploadPolicy(50, 25 * 1024 * 1024));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            "user-b",
            _document.Id,
            ValidRequest(),
            CancellationToken.None));

        Assert.Empty(_document.Pages);
        Assert.Empty(repository.Uploads);
    }

    [Fact]
    public async Task Create_RejectsDocumentAtConfiguredPageLimit()
    {
        _document.AddPage(Guid.NewGuid(), 1, Now);
        var repository = new InMemoryUploadIntentRepository(_document);
        var handler = new CreateUploadIntent(
            repository,
            new RecordingObjectStore(),
            new FixedClock(Now),
            new UploadPolicy(1, 25 * 1024 * 1024));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            "user-a",
            _document.Id,
            ValidRequest(),
            CancellationToken.None));

        Assert.Single(_document.Pages);
        Assert.Empty(repository.Uploads);
    }

    [Fact]
    public async Task Create_DoesNotCommitReservationWhenSigningFails()
    {
        var repository = new InMemoryUploadIntentRepository(_document);
        var handler = new CreateUploadIntent(
            repository,
            new ThrowingObjectStore(),
            new FixedClock(Now),
            new UploadPolicy(50, 25 * 1024 * 1024));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            "user-a",
            _document.Id,
            ValidRequest(),
            CancellationToken.None));

        Assert.Equal(0, repository.SaveCalls);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static CreateUploadRequest ValidRequest() =>
        new("scan.pdf", "application/pdf", 1200, new string('a', 64));

    private sealed class InMemoryUploadIntentRepository(Document document) : IUploadIntentRepository
    {
        public List<UploadIntent> Uploads { get; } = [];
        public int SaveCalls { get; private set; }

        public Task<Document?> FindOwnedDocumentAsync(
            string ownerFirebaseUid,
            Guid documentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Document?>(
                document.Id == documentId && document.OwnerFirebaseUid == ownerFirebaseUid ? document : null);

        public Task AddAsync(
            UploadIntent uploadIntent,
            Page page,
            CancellationToken cancellationToken)
        {
            Uploads.Add(uploadIntent);
            return Task.CompletedTask;
        }

        public Task<UploadIntent?> FindOwnedUploadAsync(
            string ownerFirebaseUid,
            Guid documentId,
            Guid uploadId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObjectStore : IObjectStore
    {
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) =>
            Task.FromException<Uri>(new InvalidOperationException("Signing failed."));

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingObjectStore : IObjectStore
    {
        public PutObjectRequest? LastRequest { get; private set; }

        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new Uri("https://uploads.example.test/opaque"));
        }

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PromoteAsync(
            string quarantineKey,
            string acceptedKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
