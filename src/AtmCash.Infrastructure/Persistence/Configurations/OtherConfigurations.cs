using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;
using AtmCash.Domain.Reconciliations;
using AtmCash.Domain.Withdrawals;
using AtmCash.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmCash.Infrastructure.Persistence.Configurations;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("Quotes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.CardHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(q => q.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.ConfirmedOption).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(q => new { q.CardHash, q.Currency, q.Status });
        builder.HasIndex(q => new { q.AtmId, q.Status });
        builder.HasIndex(q => new { q.Status, q.ExpiresAt });

        builder.HasOne<Atm>().WithMany().HasForeignKey(q => q.AtmId).OnDelete(DeleteBehavior.Restrict);

        // Offered options are a small read-only document: stored as JSON.
        builder.OwnsMany(q => q.Options, options =>
        {
            options.ToJson("Options");
            options.OwnsMany(o => o.Notes);
        });
        builder.Navigation(q => q.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(q => q.Reservations, reservations =>
        {
            reservations.ToTable("QuoteReservations");
            reservations.WithOwner().HasForeignKey("QuoteId");
            reservations.Property<int>("Id");
            reservations.HasKey("Id");
        });
        builder.Navigation(q => q.Reservations).HasField("_reservations").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(q => q.DomainEvents);
        builder.Ignore(q => q.IsExactMatch);
    }
}

internal sealed class ReconciliationConfiguration : IEntityTypeConfiguration<Reconciliation>
{
    public void Configure(EntityTypeBuilder<Reconciliation> builder)
    {
        builder.ToTable("Reconciliations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SubmittedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ReviewedBy).HasMaxLength(100);
        builder.Property(r => r.TotalDifferenceTry).HasPrecision(18, 2);
        builder.HasIndex(r => new { r.AtmId, r.SubmittedAt });

        builder.HasOne<Atm>().WithMany().HasForeignKey(r => r.AtmId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(r => r.Lines, lines =>
        {
            lines.ToTable("ReconciliationLines");
            lines.WithOwner().HasForeignKey("ReconciliationId");
            lines.Property<int>("Id");
            lines.HasKey("Id");
            lines.Property(l => l.Currency).HasConversion<string>().HasMaxLength(3);
            lines.Property(l => l.DifferenceValueTry).HasPrecision(18, 2);
        });
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <summary>Illustrative seed rates; update them through PUT /api/exchange-rates/{currency}.</summary>
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");
        builder.HasKey(r => r.Currency);
        builder.Property(r => r.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(r => r.RateToTry).HasPrecision(18, 6);

        builder.HasData(
            new ExchangeRate(Currency.TRY, 1m, SeededAt),
            new ExchangeRate(Currency.USD, 49.33m, SeededAt),
            new ExchangeRate(Currency.EUR, 55.16m, SeededAt));
    }
}

internal sealed class ExchangeRateChangeConfiguration : IEntityTypeConfiguration<ExchangeRateChange>
{
    public void Configure(EntityTypeBuilder<ExchangeRateChange> builder)
    {
        builder.ToTable("ExchangeRateChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(c => c.OldRateToTry).HasPrecision(18, 6);
        builder.Property(c => c.NewRateToTry).HasPrecision(18, 6);
        builder.Property(c => c.ChangedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => new { c.Currency, c.ChangedAt });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(200);
        builder.Property(r => r.RequestFingerprint).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ResponseJson).IsRequired();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.HasIndex(m => m.OccurredAt).HasFilter("[SentAt] IS NULL");
    }
}
