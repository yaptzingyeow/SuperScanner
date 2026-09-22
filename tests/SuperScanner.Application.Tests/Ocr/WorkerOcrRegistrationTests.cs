using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;
using SuperScanner.Worker;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class WorkerOcrRegistrationTests
{
    [Fact]
    public void AddOcrServices_ResolvesDisabledProvider()
    {
        using var services = Build("Development", enabled: false, OcrProviderNames.Disabled);

        Assert.IsType<DisabledOcrProvider>(services.GetRequiredService<IOcrProvider>());
    }

    [Fact]
    public void AddOcrServices_ResolvesFakeProviderOnlyOutsideProduction()
    {
        using var services = Build("E2E", enabled: true, OcrProviderNames.Fake);

        Assert.IsType<FakeOcrProvider>(services.GetRequiredService<IOcrProvider>());
    }

    [Fact]
    public void AddOcrServices_ResolvesGoogleProviderWithoutConstructingAdcDuringRegistration()
    {
        var collection = new ServiceCollection();
        collection.AddOcrServices(Configuration(enabled: true, OcrProviderNames.GoogleDocumentAi),
            new EnvironmentStub("Development"));
        collection.AddSingleton<IDocumentAiClient, OfflineClient>();
        using var services = collection.BuildServiceProvider();

        Assert.IsType<GoogleDocumentAiOcrProvider>(services.GetRequiredService<IOcrProvider>());
    }

    [Theory]
    [InlineData("Development", true, "Unknown")]
    [InlineData("Production", true, "Fake")]
    [InlineData("Development", true, "GoogleDocumentAi", "")]
    public void AddOcrServices_RejectsInvalidProviderOrGoogleConfiguration(
        string environment,
        bool enabled,
        string provider,
        string processorId = "fc0b14e64c62e7aa")
    {
        var configuration = Configuration(enabled, provider, processorId);

        Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddOcrServices(configuration, new EnvironmentStub(environment)));
    }

    private static ServiceProvider Build(string environment, bool enabled, string provider)
    {
        var collection = new ServiceCollection();
        collection.AddOcrServices(Configuration(enabled, provider), new EnvironmentStub(environment));
        return collection.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        bool enabled,
        string provider,
        string processorId = "fc0b14e64c62e7aa") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ocr:Enabled"] = enabled.ToString(),
            ["Ocr:Provider"] = provider,
            ["Ocr:Language"] = "en",
            ["Ocr:MaxAttempts"] = "3",
            ["Ocr:TimeoutSeconds"] = "30",
            ["Ocr:MaxElements"] = "10000",
            ["Ocr:MaxRecognizedCharacters"] = "1000000",
            ["Ocr:Google:ProjectId"] = "superscanner-dev",
            ["Ocr:Google:Location"] = "asia-southeast1",
            ["Ocr:Google:ProcessorId"] = processorId,
            ["Ocr:Google:Endpoint"] = "asia-southeast1-documentai.googleapis.com",
            ["Ocr:Google:MaxInputBytes"] = "26214400"
        }).Build();

    private sealed class OfflineClient : IDocumentAiClient
    {
        public Task<ProcessResponse> ProcessAsync(
            ByteString content,
            string mediaType,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "SuperScanner.Worker.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
