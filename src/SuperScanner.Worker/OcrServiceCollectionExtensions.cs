using Microsoft.Extensions.Options;
using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Worker;

public static class OcrServiceCollectionExtensions
{
    public static IServiceCollection AddOcrServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(OcrOptions.SectionName).Get<OcrOptions>()
            ?? new OcrOptions();
        if (!options.IsValid(environment.EnvironmentName))
        {
            throw new OptionsValidationException(
                OcrOptions.SectionName,
                typeof(OcrOptions),
                ["OCR configuration is invalid."]);
        }

        services.AddSingleton<IOptions<OcrOptions>>(Options.Create(options));
        services.AddSingleton(options.Google);
        services.AddSingleton<OcrMetrics>();

        switch (options.Provider)
        {
            case OcrProviderNames.Disabled:
                services.AddSingleton<IOcrProvider, DisabledOcrProvider>();
                break;
            case OcrProviderNames.Fake:
                services.AddSingleton<IOcrProvider, FakeOcrProvider>();
                break;
            case OcrProviderNames.GoogleDocumentAi:
                services.AddSingleton<IDocumentAiClient>(serviceProvider =>
                    DocumentAiClient.Create(serviceProvider.GetRequiredService<GoogleDocumentAiOptions>()));
                services.AddSingleton<IOcrProvider>(serviceProvider =>
                    new GoogleDocumentAiOcrProvider(
                        serviceProvider.GetRequiredService<IDocumentAiClient>(),
                        serviceProvider.GetRequiredService<GoogleDocumentAiOptions>(),
                        serviceProvider.GetRequiredService<OcrMetrics>()));
                break;
            default:
                throw new OptionsValidationException(
                    OcrOptions.SectionName,
                    typeof(OcrOptions),
                    ["OCR provider is not supported."]);
        }

        return services;
    }
}
