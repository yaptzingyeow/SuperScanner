using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.Tests.Ocr;

public sealed class OcrCarryForwardTests
{
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid NewId = Guid.NewGuid();

    // Block "This Agreement is made\nBetween" with words This/Agreement/is/made and Between.
    private static (List<OcrElement> All, Dictionary<string, OcrElement> Words) Source()
    {
        var all = new List<OcrElement>();
        var words = new Dictionary<string, OcrElement>();
        var block = Add(all, null, OcrElementKind.Block, "This Agreement is made\nBetween", 0, .1, .1, .7, .3);
        var first = Add(all, block.Id, OcrElementKind.Line, "This Agreement is made", 0, .1, .1, .7, .15);
        string[] texts = ["This", "Agreement", "is", "made"];
        for (var i = 0; i < texts.Length; i++)
            words[texts[i]] = Add(all, first.Id, OcrElementKind.Word, texts[i], i, .1 + i * .15, .1, .22 + i * .15, .15);
        var second = Add(all, block.Id, OcrElementKind.Line, "Between", 1, .3, .25, .5, .3);
        words["Between"] = Add(all, second.Id, OcrElementKind.Word, "Between", 0, .3, .25, .5, .3);
        return (all, words);
    }

    private static OcrElement Add(List<OcrElement> all, Guid? parent, OcrElementKind kind, string text, int order,
        double x0, double y0, double x1, double y1)
    {
        var element = OcrElement.Create(Guid.NewGuid(), SourceId, parent, kind, text, .9, OcrTextType.Printed, order,
            [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)]);
        all.Add(element);
        return element;
    }

    private static List<string> WordTexts(OcrCarryForward.Result result) =>
        result.Elements.Where(e => e.Kind == OcrElementKind.Word).Select(e => e.Text).ToList();

    [Fact]
    public void Replacing_a_word_keeps_every_other_word_and_its_position_and_adds_the_new_text()
    {
        var (all, words) = Source();

        var result = OcrCarryForward.Build(NewId, all, new HashSet<Guid> { words["made"].Id },
            new NormalizedBox(.6, .1, .2, .05), "signed today");

        Assert.Equal(["This", "Agreement", "is", "Between", "signed", "today"], WordTexts(result));
        Assert.Equal("This Agreement is\nBetween\n\nsigned today", result.FullText);
        var agreement = result.Elements.Single(e => e.Text == "Agreement");
        Assert.Equal(words["Agreement"].Polygon, agreement.Polygon);
        Assert.All(result.Elements, e => Assert.Equal(NewId, e.PageOcrResultId));
        Assert.DoesNotContain(result.Elements, e => all.Any(old => old.Id == e.Id));
    }

    [Fact]
    public void New_words_share_the_text_box_by_their_length()
    {
        var (all, words) = Source();

        var result = OcrCarryForward.Build(NewId, all, new HashSet<Guid> { words["made"].Id },
            new NormalizedBox(.6, .1, .2, .05), "ab cd");

        var ab = result.Elements.Single(e => e.Text == "ab").Polygon;
        var cd = result.Elements.Single(e => e.Text == "cd").Polygon;
        Assert.Equal(.6, ab[0].X, 6);
        Assert.Equal(.6 + .2 * 2 / 5, ab[1].X, 6);
        Assert.Equal(.6 + .2 * 3 / 5, cd[0].X, 6);
        Assert.Equal(.8, cd[1].X, 6);
        Assert.Equal(.1, cd[0].Y, 6);
        Assert.Equal(.15, cd[2].Y, 6);
    }

    [Fact]
    public void Deleting_every_word_of_a_line_drops_the_line_and_shrinks_the_block()
    {
        var (all, words) = Source();

        var result = OcrCarryForward.Build(NewId, all, new HashSet<Guid> { words["Between"].Id }, null, "");

        Assert.Equal(["This", "Agreement", "is", "made"], WordTexts(result));
        var block = result.Elements.Single(e => e.Kind == OcrElementKind.Block);
        Assert.Equal("This Agreement is made", block.Text);
        Assert.Equal(.15, block.Polygon[2].Y, 6);
        Assert.Single(result.Elements, e => e.Kind == OcrElementKind.Line);
    }

    [Fact]
    public void Unchanged_text_keeps_the_original_wording_and_box()
    {
        var (all, _) = Source();

        var result = OcrCarryForward.Build(NewId, all, new HashSet<Guid>(), null, "");

        Assert.Equal(all.Count, result.Elements.Count);
        Assert.Equal("This Agreement is made\nBetween", result.FullText);
        var block = result.Elements.Single(e => e.Kind == OcrElementKind.Block);
        Assert.Equal(all[0].Polygon, block.Polygon);
    }

    [Fact]
    public void Adding_text_on_a_page_without_words_creates_its_own_block()
    {
        var result = OcrCarryForward.Build(NewId, [], new HashSet<Guid>(),
            new NormalizedBox(.2, .4, .3, .1), "Paid\nThank you");

        Assert.Equal("Paid\nThank you", result.FullText);
        Assert.Equal(2, result.Elements.Count(e => e.Kind == OcrElementKind.Line));
        Assert.Equal(["Paid", "Thank", "you"], WordTexts(result));
    }
}
