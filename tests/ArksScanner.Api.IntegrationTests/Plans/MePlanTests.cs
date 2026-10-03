using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class MePlanTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
    {
        builder.UseSetting("Plans:Phase", "Enforced");
        builder.UseSetting("Plans:FreeOcrPagesPerDay", "5");
    });
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task Me_plan_returns_limits_usage_and_reset()
    {
        using var client = fixture.Client("member");

        var body = await client.GetFromJsonAsync<JsonElement>("/api/me/plan");

        Assert.Equal("Free", body.GetProperty("plan").GetString());
        Assert.Equal("Enforced", body.GetProperty("phase").GetString());
        Assert.True(body.GetProperty("brandStamp").GetBoolean());
        Assert.Equal(5, body.GetProperty("limits").GetProperty("ocrPagesPerDay").GetInt32());
        Assert.Equal(3, body.GetProperty("limits").GetProperty("watermarkExportsPerDay").GetInt32());
        Assert.Equal(30, body.GetProperty("limits").GetProperty("maxDocuments").GetInt32());
        Assert.Equal(7, body.GetProperty("limits").GetProperty("retentionDays").GetInt32());
        Assert.Equal(0, body.GetProperty("usage").GetProperty("ocrPages").GetInt32());
        Assert.True(body.GetProperty("usage").GetProperty("resetsAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(0, body.GetProperty("documentCount").GetInt32());
    }
}
