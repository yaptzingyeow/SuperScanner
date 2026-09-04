using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Processing;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("processing_jobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Type).HasMaxLength(64).IsRequired();
        builder.Property(job => job.Payload).HasMaxLength(256).IsRequired();
        builder.Property(job => job.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.AvailableAt).IsRequired();
        builder.Property(job => job.UpdatedAt).IsRequired();
        builder.Property(job => job.AttemptCount).IsRequired();
        builder.Property(job => job.WorkerId).HasMaxLength(128);
        builder.Property(job => job.LeaseExpiresAt);
        builder.Property(job => job.ErrorCode).HasMaxLength(64);
        builder.HasIndex(job => job.IdempotencyKey).IsUnique();
        builder.HasIndex(job => new { job.Status, job.AvailableAt, job.CreatedAt });
    }
}
