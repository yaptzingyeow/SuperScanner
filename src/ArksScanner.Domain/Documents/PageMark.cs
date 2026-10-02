using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ArksScanner.Domain.Documents;

public enum PageMarkKind { Check, Cross }

public sealed record PageMarkStyle
{
    public string Color { get; private init; }
    public double StrokeWidth { get; private init; }

    public PageMarkStyle(string color, double strokeWidth)
    {
        if (color is null || !Regex.IsMatch(color, @"\A#[0-9a-fA-F]{6}\z"))
            throw new ArgumentException("Mark color must be #RRGGBB.", nameof(color));
        if (!double.IsFinite(strokeWidth) || strokeWidth is < .02 or > .20)
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        Color = color.ToUpperInvariant();
        StrokeWidth = strokeWidth;
    }
}

public sealed class PageMark
{
    private PageMark() { }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid PageId { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public string? CreateRequestHash { get; private set; }
    public PageMarkKind Kind { get; private set; }
    public SignatureBox Box { get; private set; } = null!;
    public PageMarkStyle Style { get; private set; } = null!;
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static PageMark Create(Guid id, Guid documentId, Guid pageId, Guid clientRequestId,
        PageMarkKind kind, SignatureBox box, PageMarkStyle style, DateTimeOffset now)
    {
        if (id == Guid.Empty || documentId == Guid.Empty || pageId == Guid.Empty || clientRequestId == Guid.Empty)
            throw new ArgumentException("Non-empty mark identifiers are required.");
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(style);
        return new PageMark { Id = id, DocumentId = documentId, PageId = pageId,
            ClientRequestId = clientRequestId, CreateRequestHash = HashRequest(kind, box, style),
            Kind = kind, Box = box, Style = style,
            CreatedAt = now, UpdatedAt = now };
    }

    public bool MatchesOriginalCreate(PageMarkKind kind, SignatureBox box, PageMarkStyle style) =>
        CreateRequestHash is null || CreateRequestHash == HashRequest(kind, box, style);

    private static string HashRequest(PageMarkKind kind, SignatureBox box, PageMarkStyle style)
    {
        var canonical = FormattableString.Invariant($"{(int)kind}|{box.X:R}|{box.Y:R}|{box.Width:R}|{box.Height:R}|{style.Color}|{style.StrokeWidth:R}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Update(PageMarkKind kind, SignatureBox box, PageMarkStyle style, long expectedRevision, DateTimeOffset now)
    {
        EnsureEditable(expectedRevision);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(style);
        Kind = kind; Box = box; Style = style; Revision++; UpdatedAt = now;
    }

    public void Delete(long expectedRevision, DateTimeOffset now)
    {
        EnsureEditable(expectedRevision);
        DeletedAt = now; UpdatedAt = now; Revision++;
    }

    private void EnsureEditable(long expectedRevision)
    {
        if (DeletedAt is not null || Revision != expectedRevision)
            throw new InvalidOperationException("Mark was deleted or its revision changed.");
    }
}
