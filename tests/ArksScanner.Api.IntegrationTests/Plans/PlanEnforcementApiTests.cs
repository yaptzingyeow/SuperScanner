using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class PlanEnforcementApiTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
    {
        builder.UseSetting("Plans:Phase", "Enforced");
        builder.UseSetting("Plans:FreeMaxDocuments", "2");
    });
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task Document_over_the_free_limit_returns_429_documents_limit()
    {
        using var client = fixture.Client("member");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/documents", new { title = $"Doc {i}" })).StatusCode);

        var response = await client.PostAsJsonAsync("/api/documents", new { title = "One too many" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("plan_limit_reached", body.GetProperty("code").GetString());
        Assert.Equal("documents", body.GetProperty("kind").GetString());
        Assert.Equal(2, body.GetProperty("limit").GetInt32());
    }
}
