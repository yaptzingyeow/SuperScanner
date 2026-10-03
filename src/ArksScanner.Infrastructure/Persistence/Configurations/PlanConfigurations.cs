using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(x => x.FirebaseUid);
        builder.Property(x => x.FirebaseUid).HasMaxLength(128);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.SignInProvider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LastPlan).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.LastSeenAt);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountUid).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(Subscription.MaximumNoteLength);
        builder.Property(x => x.GrantedByUid).HasMaxLength(128);
        builder.Property(x => x.RevokedByUid).HasMaxLength(128);
        builder.HasIndex(x => new { x.AccountUid, x.Status });
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountUid).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdminMemberConfiguration : IEntityTypeConfiguration<AdminMember>
{
    public void Configure(EntityTypeBuilder<AdminMember> builder)
    {
        builder.ToTable("admins");
        builder.HasKey(x => x.AccountUid);
        builder.Property(x => x.AccountUid).HasMaxLength(128);
        builder.Property(x => x.AddedByUid).HasMaxLength(128);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountUid).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlanSettingsConfiguration : IEntityTypeConfiguration<PlanSettings>
{
    public void Configure(EntityTypeBuilder<PlanSettings> builder)
    {
        builder.ToTable("plan_settings", table => table.HasCheckConstraint("CK_plan_settings_singleton", "\"Id\" = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Phase).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.UsageTimeZone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OcrCostPerThousandPages).HasPrecision(10, 4);
        builder.Property(x => x.UpdatedByUid).HasMaxLength(128);
        builder.Ignore(x => x.Values);
    }
}

public sealed class UsageDayConfiguration : IEntityTypeConfiguration<UsageDay>
{
    public void Configure(EntityTypeBuilder<UsageDay> builder)
    {
        builder.ToTable("usage_days");
        builder.HasKey(x => new { x.AccountUid, x.Day });
        builder.Property(x => x.AccountUid).HasMaxLength(128);
        builder.HasIndex(x => x.Day);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountUid).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
    }
}
