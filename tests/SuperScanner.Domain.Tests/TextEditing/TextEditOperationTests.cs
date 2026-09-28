using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Domain.Tests.TextEditing;

public sealed class TextEditOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completed_edit_pins_result_revision_and_cannot_complete_twice()
    {
        var edit = Queued();
        edit.Start(Now.AddSeconds(1));
        var revisionId = Guid.NewGuid();
        edit.Complete(revisionId, Now.AddSeconds(2));

        Assert.Equal(TextEditState.Succeeded, edit.State);
        Assert.Equal(revisionId, edit.ResultRevisionId);
        Assert.Throws<InvalidOperationException>(() =>
            edit.Complete(Guid.NewGuid(), Now.AddSeconds(3)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Queue_allows_empty_replacement_for_selected_words(string replacement)
    {
        var edit = Queued(replacementText: replacement);

        Assert.Equal(replacement, edit.ReplacementText);
    }

    [Fact]
    public void Queue_rejects_oversized_replacement()
    {
        Assert.Throws<ArgumentException>(() =>
            Queued(replacementText: new string('x', TextEditOperation.MaximumReplacementLength + 1)));
    }

    [Theory]
    [InlineData(-0.1, 0.1, 0.2, 0.2)]
    [InlineData(0.1, -0.1, 0.2, 0.2)]
    [InlineData(0.9, 0.1, 0.2, 0.2)]
    [InlineData(0.1, 0.9, 0.2, 0.2)]
    [InlineData(0.1, 0.1, 0, 0.2)]
    public void Normalized_box_rejects_invalid_page_geometry(
        double x, double y, double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedBox(x, y, width, height));
    }

    [Theory]
    [InlineData("../../arial.ttf")]
    [InlineData("Arial, sans-serif")]
    [InlineData("url(font.woff2)")]
    public void Style_rejects_arbitrary_font_identifiers(string fontId)
    {
        Assert.Throws<ArgumentException>(() => ValidStyle(fontId));
    }

    [Fact]
    public void Style_rejects_non_finite_or_out_of_range_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidStyle(fontSize: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidStyle(weight: 950));
        Assert.Throws<ArgumentException>(() => ValidStyle(colorHex: "black"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidStyle(letterSpacing: double.PositiveInfinity));
    }

    [Fact]
    public void Illegal_state_transitions_are_rejected()
    {
        var queued = Queued();
        Assert.Throws<InvalidOperationException>(() => queued.Complete(Guid.NewGuid(), Now));

        queued.Start(Now.AddSeconds(1));
        Assert.Throws<InvalidOperationException>(() => queued.Start(Now.AddSeconds(2)));
    }

    [Theory]
    [InlineData("Contains Spaces")]
    [InlineData("UPPER_CASE")]
    [InlineData("unsafe/error")]
    public void Failure_codes_are_safe_identifiers(string failureCode)
    {
        var edit = Queued();
        edit.Start(Now.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => edit.Fail(failureCode, Now.AddSeconds(2)));
    }

    [Fact]
    public void Queue_requires_canonical_hash_and_idempotency_key()
    {
        Assert.Throws<ArgumentException>(() => Queued(requestHash: "not-a-hash"));
        Assert.Throws<ArgumentException>(() => Queued(idempotencyKey: " "));

        var edit = Queued(requestHash: new string('a', 64), idempotencyKey: "apply-1");

        Assert.Equal(new string('a', 64), edit.CanonicalRequestHash);
        Assert.Equal("apply-1", edit.IdempotencyKey);
    }

    private static TextEditOperation Queued(
        string replacementText = "Tan BB",
        string requestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string idempotencyKey = "apply-1") =>
        TextEditOperation.Queue(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "owner",
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()],
            "Yap Tzing Yeow", replacementText,
            new NormalizedBox(0.1, 0.2, 0.3, 0.1),
            ValidStyle(), 1, null, idempotencyKey, requestHash,
            "renderer-v1", "layout-v1", Now);

    private static TextEditStyle ValidStyle(
        string fontId = "noto-sans",
        double fontSize = 0.04,
        int weight = 400,
        string colorHex = "#112233",
        double letterSpacing = 0) =>
        new(fontId, "v1", fontSize, weight, colorHex, letterSpacing,
            0.25, 0, TextAlignment.Left);
}
