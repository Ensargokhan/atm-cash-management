using AtmCash.Domain.Atms;
using AtmCash.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCash.Infrastructure.Persistence.Configurations;

internal sealed class AtmConfiguration : IEntityTypeConfiguration<Atm>
{
    public void Configure(EntityTypeBuilder<Atm> builder)
    {
        builder.ToTable("Atms");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(a => a.Code).IsUnique();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Location).HasMaxLength(200);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(a => a.Cassettes)
            .WithOne()
            .HasForeignKey(c => c.AtmId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Cassettes).HasField("_cassettes").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.DepositCassette);
    }
}

internal sealed class CassetteConfiguration : IEntityTypeConfiguration<Cassette>
{
    public void Configure(EntityTypeBuilder<Cassette> builder)
    {
        builder.ToTable("Cassettes", t =>
        {
            t.HasCheckConstraint("CK_Cassettes_Count", "[Count] >= 0 AND [Count] <= [Capacity]");
            t.HasCheckConstraint("CK_Cassettes_Reserved", "[ReservedCount] >= 0 AND [ReservedCount] <= [Count]");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasIndex(c => new { c.AtmId, c.Slot }).IsUnique();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Currency).HasConversion<string>().HasMaxLength(3);

        builder.OwnsMany(c => c.Items, items =>
        {
            items.ToTable("DepositCassetteItems");
            items.WithOwner().HasForeignKey("CassetteId");
            items.Property<int>("Id");
            items.HasKey("Id");
            items.Property(i => i.Currency).HasConversion<string>().HasMaxLength(3);
            items.HasIndex("CassetteId", nameof(DepositCassetteItem.Currency), nameof(DepositCassetteItem.Denomination)).IsUnique();
        });
        builder.Navigation(c => c.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AtmStatusChangeConfiguration : IEntityTypeConfiguration<AtmStatusChange>
{
    public void Configure(EntityTypeBuilder<AtmStatusChange> builder)
    {
        builder.ToTable("AtmStatusChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.PerformedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => new { c.AtmId, c.OccurredAt });

        builder.HasOne<Atm>().WithMany().HasForeignKey(c => c.AtmId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CassetteMovementConfiguration : IEntityTypeConfiguration<CassetteMovement>
{
    public const string AppendOnlyTrigger = "TR_CassetteMovements_AppendOnly";

    public void Configure(EntityTypeBuilder<CassetteMovement> builder)
    {
        // The trigger (created in the migration) rejects UPDATE and DELETE; declaring it stops EF from using OUTPUT clauses.
        builder.ToTable("CassetteMovements", t => t.HasTrigger(AppendOnlyTrigger));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(m => m.PerformedBy).HasMaxLength(100);

        builder.HasIndex(m => new { m.AtmId, m.CreatedAt });
        builder.HasIndex(m => m.CassetteId);
        builder.HasIndex(m => m.ReferenceId);

        builder.HasOne<Atm>().WithMany().HasForeignKey(m => m.AtmId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cassette>().WithMany().HasForeignKey(m => m.CassetteId).OnDelete(DeleteBehavior.Restrict);
    }
}
