using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class TextEditingEndpointsTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid PageId = Guid.NewGuid();
    private static readonly Guid ResultId = Guid.NewGuid();
    private static readonly Guid LineId = Guid.NewGuid();
    private static readonly Guid WordId = Guid.NewGuid();

    [Fact]
    public async Task Disabled_style_proposal_returns_safe_503()
    {
        await using var factory = Factory(enabled: false);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(Url(), new { ocrResultId = ResultId, wordIds = new[] { WordId } });
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Printed_word_returns_server_owned_proposal_without_cache()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(Url(), new { ocrResultId = ResultId, wordIds = new[] { WordId } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<TextStyleProposalDto>();
        Assert.Equal("Name", body?.OriginalText);
    }

    [Fact]
    public async Task Authentication_and_app_check_are_required()
    {
        await using var factory = Factory(enabled: true);
        using var missingIdentity = Client(factory, authenticated: false);
        using var missingAppCheck = Client(factory, appCheck: false);
        var payload = new { ocrResultId = ResultId, wordIds = new[] { WordId } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await missingIdentity.PostAsJsonAsync(Url(), payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await missingAppCheck.PostAsJsonAsync(Url(), payload)).StatusCode);
    }

    [Fact]
    public async Task Unowned_page_is_hidden()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var url = $"/api/documents/{Guid.NewGuid()}/pages/{PageId}/text-edits/style-proposal";
        var response = await client.PostAsJsonAsync(url, new { ocrResultId = ResultId, wordIds = new[] { WordId } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Handwriting_is_rejected_without_its_content_in_response()
    {
        await using var factory = Factory(enabled: true, type: OcrTextType.Handwritten);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(Url(), new { ocrResultId = ResultId, wordIds = new[] { WordId } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Name", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stale_ocr_returns_stable_conflict()
    {
        await using var factory = Factory(enabled: true, stale: true);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(Url(), new { ocrResultId = ResultId, wordIds = new[] { WordId } });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Empty_word_selection_returns_safe_validation_error()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(Url(), new { ocrResultId = ResultId, wordIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Name", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owned_history_is_empty_and_not_cached_before_first_edit()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var response = await client.GetAsync($"/api/documents/{DocumentId}/pages/{PageId}/text-edits/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Disabled_apply_returns_503_without_mutation()
    {
        await using var factory = Factory(enabled: false);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(ApplyUrl(), ApplyPayload());
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Enabled_apply_returns_accepted_and_no_store()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(ApplyUrl(), ApplyPayload());
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<TextEditAccepted>();
        Assert.NotEqual(Guid.Empty, body?.EditId);
    }

    [Fact]
    public async Task Apply_requires_verified_identity_and_app_check()
    {
        await using var factory = Factory(enabled: true);
        using var missingIdentity = Client(factory, authenticated: false);
        using var missingAppCheck = Client(factory, appCheck: false);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await missingIdentity.PostAsJsonAsync(ApplyUrl(), ApplyPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await missingAppCheck.PostAsJsonAsync(ApplyUrl(), ApplyPayload())).StatusCode);
    }

    [Fact]
    public async Task Accepted_edit_can_be_refetched_and_appears_in_history()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var accepted = await client.PostAsJsonAsync(ApplyUrl(), ApplyPayload());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var edit = await accepted.Content.ReadFromJsonAsync<TextEditAccepted>();
        var status = await client.GetAsync($"{ApplyUrl()}/{edit!.EditId}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadFromJsonAsync<TextEditDto>();
        Assert.Equal("Tan BB", body?.ReplacementText);
        var history = await client.GetFromJsonAsync<TextEditDto[]>($"{ApplyUrl()}/history");
        Assert.Single(history!);
    }

    [Fact]
    public async Task Unowned_apply_is_hidden_as_not_found()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var url = $"/api/documents/{Guid.NewGuid()}/pages/{PageId}/text-edits";
        var response = await client.PostAsJsonAsync(url, ApplyPayload());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stale_revision_apply_returns_conflict_without_text()
    {
        await using var factory = Factory(enabled: true);
        using var client = Client(factory);
        var response = await client.PostAsJsonAsync(ApplyUrl(),
            ApplyPayload(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Tan BB", await response.Content.ReadAsStringAsync());
    }

    private static string Url() => $"/api/documents/{DocumentId}/pages/{PageId}/text-edits/style-proposal";
    private static string ApplyUrl() => $"/api/documents/{DocumentId}/pages/{PageId}/text-edits";
    private static object ApplyPayload(Guid? expectedRevisionId = null) => new
    {
        ocrResultId = ResultId,
        expectedRevisionId,
        wordIds = new[] { WordId },
        replacementText = "Tan BB",
        replacementBox = new { x = .1, y = .2, width = .8, height = .1 },
        style = new { fontId = "noto-sans", fontVersion = "archive-main-regular",
            fontSize = .04, weight = 400, colorHex = "#000000", letterSpacing = 0,
            baseline = .25, angleDegrees = 0, alignment = 0 },
        idempotencyKey = "apply-1"
    };

    private static WebApplicationFactory<Program> Factory(bool enabled,
        OcrTextType type = OcrTextType.Printed, bool stale = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("TextEditing:Enabled", enabled.ToString());
            builder.ConfigureTestServices(services =>
            {
                var commandRepository = new CommandRepository();
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, IdentityVerifier>();
                services.RemoveAll<ITextSelectionRepository>();
                services.AddSingleton<ITextSelectionRepository>(new Repository(type, stale));
                services.RemoveAll<ITextStyleEstimator>();
                services.AddSingleton<ITextStyleEstimator>(new Estimator());
                services.RemoveAll<ITextEditReadRepository>();
                services.AddSingleton<ITextEditReadRepository>(commandRepository);
                services.RemoveAll<ITextEditCommandRepository>();
                services.AddSingleton<ITextEditCommandRepository>(commandRepository);
                services.RemoveAll<ITextEditPreparation>();
                services.AddSingleton<ITextEditPreparation>(new Preparation());
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(new Audit());
                services.RemoveAll<IProcessingJobQueue>();
                services.AddSingleton<IProcessingJobQueue>(new Queue());
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory,
        bool authenticated = true, bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "owner");
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed class IdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            idToken == "owner" && appCheckToken == "valid-app"
                ? Task.FromResult(new VerifiedRequestIdentity("owner", "owner@example.test"))
                : Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }

    private sealed class Repository(OcrTextType type, bool stale) : ITextSelectionRepository
    {
        public Task<OwnedTextSelection?> FindOwnedAsync(string ownerUid, Guid documentId,
            Guid pageId, Guid ocrResultId, CancellationToken ct)
        {
            if (ownerUid != "owner" || documentId != DocumentId || pageId != PageId || ocrResultId != ResultId)
                return Task.FromResult<OwnedTextSelection?>(null);
            var word = OcrElement.Create(WordId, ResultId, LineId, OcrElementKind.Word,
                "Name", 0.95, type, 0,
                [new(.1, .2), new(.2, .2), new(.2, .25), new(.1, .25)]);
            return Task.FromResult<OwnedTextSelection?>(new OwnedTextSelection(null, "private/page.jpg",
                ResultId, OcrResultState.Ready,
                stale ? "private/old-page.jpg" : "private/page.jpg", [word]));
        }
    }

    private sealed class Estimator : ITextStyleEstimator
    {
        public Task<TextStyleEstimate> EstimateAsync(string sourceObjectKey,
            IReadOnlyList<OcrElement> words, CancellationToken ct) =>
            Task.FromResult(new TextStyleEstimate([new("noto-sans", "archive-main-regular", 0.5)],
                0.5, "#000000", 12, 400, 0, 0, "left"));
    }

    private sealed class CommandRepository : ITextEditCommandRepository, ITextEditReadRepository
    {
        private readonly Page page;
        private readonly PageOcrResult ocr;
        private readonly List<TextEditOperation> edits = [];

        public CommandRepository()
        {
            var now = DateTimeOffset.UtcNow;
            var document = Document.Create(DocumentId, "owner", "Test", now);
            page = document.AddPage(PageId, 10, now);
            page.MarkImportReady("original.jpg", "image/jpeg");
            page.SetPreview("private/page.jpg", "thumb.jpg");
            page.MarkReady();
            ocr = PageOcrResult.Queue(ResultId, PageId, "private/page.jpg", new string('a', 64), "en", now);
            ocr.BeginAttempt(1, now);
            var line = OcrElement.Create(LineId, ResultId, null, OcrElementKind.Line,
                "Name", .95, OcrTextType.Printed, 0,
                [new(.1, .2), new(.2, .2), new(.2, .25), new(.1, .25)]);
            var word = OcrElement.Create(WordId, ResultId, LineId, OcrElementKind.Word,
                "Name", .95, OcrTextType.Printed, 0,
                [new(.1, .2), new(.2, .2), new(.2, .25), new(.1, .25)]);
            ocr.Complete("test", "v1", "Name", [line, word], now);
        }

        public Task<ITextEditTransaction> BeginAsync(CancellationToken ct) =>
            Task.FromResult<ITextEditTransaction>(new Transaction());
        public Task<LockedTextEditPage?> FindOwnedForUpdateAsync(string uid, Guid documentId,
            Guid pageId, CancellationToken ct) => Task.FromResult<LockedTextEditPage?>(
                uid == "owner" && documentId == DocumentId && pageId == PageId
                    ? new LockedTextEditPage(page, ocr) : null);
        public Task<TextEditOperation?> FindByIdempotencyAsync(Guid pageId, string key, CancellationToken ct) =>
            Task.FromResult(edits.SingleOrDefault(edit => edit.PageId == pageId && edit.IdempotencyKey == key));
        public Task<int> CountPendingAsync(Guid pageId, CancellationToken ct) => Task.FromResult(0);
        public Task<long> NextSequenceAsync(Guid pageId, CancellationToken ct) => Task.FromResult(1L);
        public Task AddBaseRevisionAsync(PageRevision revision, CancellationToken ct) => Task.CompletedTask;
        public Task AddEditAsync(TextEditOperation edit, CancellationToken ct)
        {
            edits.Add(edit);
            return Task.CompletedTask;
        }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsOwnedPageAsync(string uid, Guid documentId, Guid pageId, CancellationToken ct) =>
            Task.FromResult(uid == "owner" && documentId == DocumentId && pageId == PageId);
        public Task<TextEditOperation?> FindAsync(Guid pageId, Guid editId, CancellationToken ct) =>
            Task.FromResult(edits.SingleOrDefault(edit => edit.PageId == pageId && edit.Id == editId));
        public Task<IReadOnlyList<TextEditOperation>> ListAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TextEditOperation>>(edits.Where(edit => edit.PageId == pageId).ToArray());
        private sealed class Transaction : ITextEditTransaction
        {
            public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Preparation : ITextEditPreparation
    {
        public Task<PreparedTextEdit> PrepareAsync(string key, string replacement,
            NormalizedBox box, TextEditStyle style, CancellationToken ct) =>
            Task.FromResult(new PreparedTextEdit(new string('a', 64), true));
    }

    private sealed class Audit : IAuditWriter
    {
        public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct) =>
            Task.FromResult(Guid.NewGuid());
    }

    private sealed class Queue : IProcessingJobQueue
    {
        public Task EnqueueAsync(string type, string payload, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<ProcessingJobLease?> TryLeaseAsync(string id, TimeSpan duration, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan duration, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) => throw new NotSupportedException();
    }
}
