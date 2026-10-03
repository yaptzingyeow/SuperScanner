using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Documents;
using ArksScanner.Application.Ocr;
using ArksScanner.Application.Plans;
using ArksScanner.Application.Tests.Ocr;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Application.Tests.Plans;

public sealed class PlanEnforcementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 2, 0, 0, TimeSpan.Zero);
    private readonly Guid documentId = Guid.NewGuid();
    private readonly Guid pageId = Guid.NewGuid();

    private MemoryOcrRepository ReadyOcr() => new(new OcrPageSource(pageId, PageState.Ready, "private/preview", "image/jpeg"));

    [Fact]
    public async Task Ocr_request_over_the_free_limit_throws_and_queues_nothing()
    {
        var plans = new MemoryPlanRepository(MemoryPlanRepository.Enforced(ocr: 0));
        var queue = new RecordingQueue();
        var command = new RequestPageOcr(ReadyOcr(), queue, new FixedClock(Now), new PlanService(plans, new FixedClock(Now)));

        var limit = await Assert.ThrowsAsync<PlanLimitExceededException>(() =>
            command.HandleAsync("owner", documentId, pageId, false, default));

        Assert.Equal("ocr", limit.KindCode);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Reading_an_existing_ocr_result_does_not_count()
    {
        var plans = new MemoryPlanRepository(MemoryPlanRepository.Enforced(ocr: 1));
        var command = new RequestPageOcr(ReadyOcr(), new RecordingQueue(), new FixedClock(Now), new PlanService(plans, new FixedClock(Now)));

        await command.HandleAsync("owner", documentId, pageId, false, default);
        await command.HandleAsync("owner", documentId, pageId, false, default);

        Assert.Equal(1, plans.Total(UsageKind.Ocr));
    }

    [Fact]
    public async Task Document_over_the_free_limit_throws_documents_limit()
    {
        var plans = new MemoryPlanRepository(MemoryPlanRepository.Enforced(documents: 30)) { ActiveDocuments = 30 };
        var service = new PlanService(plans, new FixedClock(Now));

        var limit = await Assert.ThrowsAsync<PlanLimitExceededException>(() => service.EnsureCanCreateDocumentAsync("owner", default));

        Assert.Equal("documents", limit.KindCode);
        Assert.Equal(30, limit.Limit);
    }
}
