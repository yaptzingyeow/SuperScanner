using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class DocumentAiClientTests
{
    [Fact]
    public async Task Create_UsesRegionalEndpointAndBuildsExactProcessRequest()
    {
        const string expectedEndpoint = "asia-southeast1-documentai.googleapis.com";
        const string expectedName =
            "projects/superscanner-dev/locations/asia-southeast1/processors/fc0b14e64c62e7aa";
        var expectedContent = ByteString.CopyFrom([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        string? receivedEndpoint = null;
        ProcessRequest? receivedRequest = null;
        CancellationToken receivedCancellation = default;
        var expectedResponse = new ProcessResponse();

        var client = DocumentAiClient.Create(
            ValidOptions(),
            endpoint =>
            {
                receivedEndpoint = endpoint;
                return (request, ct) =>
                {
                    receivedRequest = request;
                    receivedCancellation = ct;
                    return Task.FromResult(expectedResponse);
                };
            });

        var response = await client.ProcessAsync(expectedContent, "image/png", cancellation.Token);

        Assert.Same(expectedResponse, response);
        Assert.Equal(expectedEndpoint, receivedEndpoint);
        Assert.NotNull(receivedRequest);
        Assert.Equal(expectedName, receivedRequest.Name);
        Assert.Equal(expectedContent, receivedRequest.RawDocument.Content);
        Assert.Equal("image/png", receivedRequest.RawDocument.MimeType);
        Assert.Equal(cancellation.Token, receivedCancellation);
    }

    private static GoogleDocumentAiOptions ValidOptions() => new()
    {
        ProjectId = "superscanner-dev",
        Location = "asia-southeast1",
        ProcessorId = "fc0b14e64c62e7aa",
        Endpoint = "asia-southeast1-documentai.googleapis.com"
    };
}
