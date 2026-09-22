using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfDocumentRepository(AppDbContext db) : IDocumentRepository
{
    private Document? lockedDocument;

    public async Task<IDocumentTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new DocumentTransaction(this, await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task<Document?> FindOwnedForUpdateAsync(string ownerUid, Guid documentId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A document mutation requires a transaction.");

        var documents = await db.Documents.FromSqlInterpolated(
            $"SELECT * FROM documents WHERE \"Id\" = {documentId} AND \"OwnerFirebaseUid\" = {ownerUid} FOR UPDATE")
            .ToListAsync(cancellationToken);
        lockedDocument = documents.SingleOrDefault();
        if (lockedDocument is not null)
            await db.Entry(lockedDocument).Collection(d => d.Pages).Query()
                .Where(p => p.RemovedAt == null).LoadAsync(cancellationToken);
        return lockedDocument;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || lockedDocument is null)
            throw new InvalidOperationException("A locked document mutation is required.");

        // The partial unique index is immediate. Park persisted active positions below zero
        // so swaps and removal compaction cannot collide with a row awaiting its EF update.
        // These temporary values are invisible outside this transaction and roll back on failure.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE pages SET \"Position\" = -\"Position\" WHERE \"DocumentId\" = {lockedDocument.Id} AND \"RemovedAt\" IS NULL",
            cancellationToken);
        foreach (var page in lockedDocument.Pages)
            db.Entry(page).Property(p => p.Position).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed class DocumentTransaction(EfDocumentRepository repository, IDbContextTransaction transaction) : IDocumentTransaction
    {
        private bool committed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            repository.lockedDocument = null;
            if (!committed) repository.Context.ChangeTracker.Clear();
        }

    }

    private AppDbContext Context => db;

    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> ListByOwnerAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken) =>
        await db.Documents
            .AsNoTracking()
            .Include(document => document.Pages)
            .Where(document => document.OwnerFirebaseUid == ownerFirebaseUid)
            .OrderByDescending(document => document.UpdatedAt)
            .ToListAsync(cancellationToken);
}
