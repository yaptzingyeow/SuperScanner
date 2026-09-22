using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;

namespace SuperScanner.Infrastructure.Ocr;

public delegate Task<ProcessResponse> ProcessDocumentDelegate(
    ProcessRequest request,
    CancellationToken cancellationToken);

public interface IDocumentAiClient
{
    Task<ProcessResponse> ProcessAsync(
        ByteString content,
        string mediaType,
        CancellationToken cancellationToken);
}

public sealed class DocumentAiClient : IDocumentAiClient
{
    private readonly GoogleDocumentAiOptions options;
    private readonly ProcessDocumentDelegate processDocument;

    private DocumentAiClient(
        GoogleDocumentAiOptions options,
        ProcessDocumentDelegate processDocument)
    {
        this.options = options;
        this.processDocument = processDocument;
    }

    public static DocumentAiClient Create(GoogleDocumentAiOptions options) =>
        Create(options, (endpoint, settings) =>
        {
            var client = new DocumentProcessorServiceClientBuilder
            {
                Endpoint = endpoint,
                Settings = settings
            }.Build();

            return (request, cancellationToken) =>
                client.ProcessDocumentAsync(request, cancellationToken);
        });

    public static DocumentAiClient Create(
        GoogleDocumentAiOptions options,
        Func<string, DocumentProcessorServiceSettings, ProcessDocumentDelegate> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transportFactory);

        var settings = new DocumentProcessorServiceSettings
        {
            ProcessDocumentSettings = CallSettings.FromExpiration(Expiration.None)
        };

        return new DocumentAiClient(options,
            transportFactory(options.EffectiveEndpoint, settings));
    }

    public Task<ProcessResponse> ProcessAsync(
        ByteString content,
        string mediaType,
        CancellationToken cancellationToken) =>
        processDocument(new ProcessRequest
        {
            Name = options.ProcessorName,
            RawDocument = new RawDocument
            {
                Content = content,
                MimeType = mediaType
            },
            ProcessOptions = options.EnableStyleInfo
                ? new ProcessOptions
                {
                    OcrConfig = new OcrConfig
                    {
#pragma warning disable CS0612 // Required until the configured processor accepts PremiumFeatures.ComputeStyleInfo.
                        ComputeStyleInfo = true
#pragma warning restore CS0612
                    }
                }
                : null
        }, cancellationToken);
}
