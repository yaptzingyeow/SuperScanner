using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Domain.Processing;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<UploadIntent> UploadIntents => Set<UploadIntent>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
