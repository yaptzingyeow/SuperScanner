using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class RetentionApiTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
        builder.UseSetting("Plans:Phase", "Enforced"));
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        var body = await (await client.PostAsJsonAsync("/api/documents", new { title })).Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Removed_documents_disappear_from_the_list_and_return_404()
    {
        using var client = fixture.Client("member");
        var kept = await CreateAsync(client, "Kept");
        var gone = await CreateAsync(client, "Gone");
        await using (var db = fixture.Db())
        {
            var document = await db.Documents.SingleAsync(d => d.Id == gone);
            document.Remove("retention", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/documents");

        Assert.Equal([kept], list!.Select(d => d.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{gone}/exports/preview")).StatusCode);
    }

    [Fact]
    public async Task List_shows_expiry_for_free_documents_in_enforced_phase()
    {
        using var client = fixture.Client("member");
        await CreateAsync(client, "Expiring");

        var item = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/documents"))!);

        var expires = item.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(6.9), DateTimeOffset.UtcNow.AddDays(7.1));
    }
}
