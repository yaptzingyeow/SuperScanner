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
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
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

    private static string Url() => $"/api/documents/{DocumentId}/pages/{PageId}/text-edits/style-proposal";

    private static WebApplicationFactory<Program> Factory(bool enabled,
        OcrTextType type = OcrTextType.Printed, bool stale = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("TextEditing:Enabled", enabled.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, IdentityVerifier>();
                services.RemoveAll<ITextSelectionRepository>();
                services.AddSingleton<ITextSelectionRepository>(new Repository(type, stale));
                services.RemoveAll<ITextStyleEstimator>();
                services.AddSingleton<ITextStyleEstimator>(new Estimator());
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
}
