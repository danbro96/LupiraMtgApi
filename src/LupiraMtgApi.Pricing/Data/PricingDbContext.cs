using LupiraMtgApi.Pricing.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Pricing.Data;

public sealed class PricingDbContext : DbContext
{
    public const string Schema = "prices";

    public PricingDbContext(DbContextOptions<PricingDbContext> options)
        : base(options)
    {
    }

    public DbSet<CardPriceLatest> CardPricesLatest => Set<CardPriceLatest>();

    public DbSet<CardPricePoint> CardPricePoints => Set<CardPricePoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CardPriceLatest>(e =>
        {
            e.ToTable("card_prices_latest");
            e.HasKey(p => p.PrintingId);
            e.Property(p => p.PrintingId).HasMaxLength(64);
            e.Property(p => p.Eur).HasColumnType("numeric(12,2)");
            e.Property(p => p.EurFoil).HasColumnType("numeric(12,2)");
            e.Property(p => p.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<CardPricePoint>(e =>
        {
            e.ToTable("card_price_points");
            e.HasKey(p => new { p.PrintingId, p.ObservedOn });
            e.Property(p => p.PrintingId).HasMaxLength(64);
            e.Property(p => p.Eur).HasColumnType("numeric(12,2)");
            e.Property(p => p.EurFoil).HasColumnType("numeric(12,2)");
            e.Property(p => p.Source).HasMaxLength(32).IsRequired();
            e.HasIndex(p => p.PrintingId);
        });
    }
}
