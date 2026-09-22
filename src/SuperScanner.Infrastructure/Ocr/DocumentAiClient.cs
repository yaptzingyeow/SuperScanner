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
        Create(options, endpoint =>
        {
            var client = new DocumentProcessorServiceClientBuilder
            {
                Endpoint = endpoint
            }.Build();

            return (request, cancellationToken) =>
                client.ProcessDocumentAsync(request, cancellationToken);
        });

    public static DocumentAiClient Create(
        GoogleDocumentAiOptions options,
        Func<string, ProcessDocumentDelegate> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transportFactory);

        return new DocumentAiClient(
            options,
            transportFactory(options.EffectiveEndpoint));
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
            }
        }, cancellationToken);
}
