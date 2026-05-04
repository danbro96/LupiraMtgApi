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

    public DbSet<SetTypeWeight> SetTypeWeights => Set<SetTypeWeight>();

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

            e.Property(p => p.Supertype).HasMaxLength(64);
            e.Property(p => p.Type).HasMaxLength(64).IsRequired();
            e.Property(p => p.Subtype).HasMaxLength(128);
            e.Property(p => p.RulesText).HasColumnType("text");
            e.Property(p => p.OracleText).HasColumnType("text");
            e.Property(p => p.Power).HasMaxLength(16);
            e.Property(p => p.Toughness).HasMaxLength(16);
            e.Property(p => p.Lang).HasMaxLength(8).IsRequired();
            e.Property(p => p.Layout).HasMaxLength(32).IsRequired();
            e.Property(p => p.IsFoil).IsRequired();

            // Recomposed type line as a Postgres GENERATED ALWAYS AS … STORED column.
            // Always equals: [Supertype ]Type[ — Subtype]. Drives the type-line trigram
            // index so OCR matching uses the whole composed string.
            e.Property(p => p.TypeLineFull)
                .HasColumnType("text")
                .HasComputedColumnSql(
                    """
                    NULLIF(TRIM(BOTH ' ' FROM
                        COALESCE("Supertype" || ' ', '')
                        || COALESCE("Type", '')
                        || CASE WHEN "Subtype" IS NULL THEN '' ELSE ' — ' || "Subtype" END
                    ), '')
                    """,
                    stored: true);

            e.HasIndex(p => p.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(p => p.TypeLineFull).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(p => p.RulesText).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(p => new { p.SetCode, p.CollectorNumber, p.Lang });
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
            e.Property(s => s.IconObjectKey).HasColumnType("text");
        });

        modelBuilder.Entity<DeviceUser>(e =>
        {
            e.ToTable("me_devices");
            e.HasKey(d => d.Sub);
            e.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(d => d.DisplayName).HasMaxLength(64);
            e.HasIndex(d => d.TokenHash).IsUnique();
        });

        modelBuilder.Entity<SetTypeWeight>(e =>
        {
            e.ToTable("set_type_weights");
            e.HasKey(w => w.SetType);
            e.Property(w => w.SetType).HasMaxLength(32);
            e.Property(w => w.Weight).IsRequired();
            e.Property(w => w.UpdatedAt).IsRequired();
        });
    }
}
