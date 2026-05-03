using LupiraMtgApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Data;

public sealed class LupiraMtgDbContext : DbContext
{
    public const string Schema = "cards";

    public LupiraMtgDbContext(DbContextOptions<LupiraMtgDbContext> options)
        : base(options)
    {
    }

    public DbSet<CardPrinting> CardPrintings => Set<CardPrinting>();

    public DbSet<ScryfallSet> Sets => Set<ScryfallSet>();

    public DbSet<DeviceUser> Devices => Set<DeviceUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<CardPrinting>(e =>
        {
            e.ToTable("card_printings");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.OracleId).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(256);
            e.Property(p => p.SetCode).HasMaxLength(16);
            e.Property(p => p.CollectorNumber).HasMaxLength(16);
            e.Property(p => p.Rarity).HasMaxLength(16);
            e.Property(p => p.ColorIdentity).HasColumnType("text[]");
            e.Property(p => p.Prices).HasColumnType("jsonb");
            e.HasIndex(p => p.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(p => new { p.SetCode, p.CollectorNumber });
            e.HasIndex(p => p.OracleId);
        });

        modelBuilder.Entity<ScryfallSet>(e =>
        {
            e.ToTable("sets");
            e.HasKey(s => s.Code);
            e.Property(s => s.Code).HasMaxLength(16);
            e.Property(s => s.Name).HasMaxLength(128);
            e.Property(s => s.SetType).HasMaxLength(32);
        });

        modelBuilder.Entity<DeviceUser>(e =>
        {
            e.ToTable("me_devices");
            e.HasKey(d => d.Sub);
            e.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(d => d.DisplayName).HasMaxLength(64);
            e.HasIndex(d => d.TokenHash).IsUnique();
        });
    }
}
