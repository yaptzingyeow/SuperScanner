using SuperScanner.Api.Auth;
using SuperScanner.Application.Documents;

namespace SuperScanner.Api.Endpoints;

public static class DocumentsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents").RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateDocumentRequest request,
        ICurrentUser currentUser,
        CreateDocument createDocument,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title must contain between 1 and 200 characters."]
            });
        }

        var document = await createDocument.HandleAsync(
            currentUser.FirebaseUid,
            title,
            cancellationToken);
        return Results.Created($"/api/documents/{document.Id}", document);
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser currentUser,
        ListDocuments listDocuments,
        CancellationToken cancellationToken) =>
        Results.Ok(await listDocuments.HandleAsync(currentUser.FirebaseUid, cancellationToken));

    public sealed record CreateDocumentRequest(string? Title);
}
