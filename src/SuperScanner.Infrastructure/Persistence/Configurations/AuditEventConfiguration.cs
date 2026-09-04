using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Auditing;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.ActorUid).HasMaxLength(128).IsRequired();
        builder.Property(auditEvent => auditEvent.Action).HasMaxLength(64).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(auditEvent => auditEvent.RegionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(auditEvent => auditEvent.OccurredAt).IsRequired();
        builder.Property(auditEvent => auditEvent.PreviousHash).HasMaxLength(32).IsRequired();
        builder.Property(auditEvent => auditEvent.EventHash).HasMaxLength(32).IsRequired();
        builder.Property(auditEvent => auditEvent.Signature).HasMaxLength(32).IsRequired();
        builder.Property(auditEvent => auditEvent.SigningKeyId).HasMaxLength(64).IsRequired();
        builder.HasIndex(auditEvent => new { auditEvent.TargetId, auditEvent.Sequence }).IsUnique();
    }
}
