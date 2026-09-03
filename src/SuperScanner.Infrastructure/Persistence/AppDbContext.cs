using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
