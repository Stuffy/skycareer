using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SkyCareer.Domain.Career;

namespace SkyCareer.Data;

/// <summary>
/// One SQLite database holds all careers (scoped by CareerId) plus the shared airport master data.
/// Enums are stored as strings so the file stays readable and reordering enum members can't corrupt saves.
/// </summary>
public sealed class SkyCareerDb(DbContextOptions<SkyCareerDb> options) : DbContext(options)
{
    public DbSet<Career> Careers => Set<Career>();
    public DbSet<FleetAircraft> Fleet => Set<FleetAircraft>();
    public DbSet<MissionContract> Contracts => Set<MissionContract>();
    public DbSet<FlightRecord> Flights => Set<FlightRecord>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();
    public DbSet<GsxReceiptRecord> GsxReceipts => Set<GsxReceiptRecord>();
    public DbSet<AirportRow> Airports => Set<AirportRow>();
    public DbSet<AirportFacilityCacheRow> AirportFacilities => Set<AirportFacilityCacheRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        // SQLite has no decimal type; store as TEXT (exact) - sums are done in memory.
        b.Properties<decimal>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Career>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.AirlineName).HasMaxLength(100);
            e.Property(x => x.HomeBase).HasMaxLength(16);
        });

        m.Entity<FleetAircraft>(e =>
        {
            e.HasIndex(x => x.CareerId);
            e.HasIndex(x => new { x.CareerId, x.Registration }).IsUnique();
            e.HasOne<Career>().WithMany().HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<MissionContract>(e =>
        {
            e.HasIndex(x => new { x.CareerId, x.Status });
            e.HasOne<Career>().WithMany().HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<FlightRecord>(e =>
        {
            e.HasIndex(x => x.CareerId);
            e.HasOne<Career>().WithMany().HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<LedgerEntry>(e =>
        {
            e.HasIndex(x => new { x.CareerId, x.GameTime });
            e.HasOne<Career>().WithMany().HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<GsxReceiptRecord>(e =>
        {
            e.HasIndex(x => new { x.CareerId, x.ReceiptId }).IsUnique();
            e.Property(x => x.ReceiptId).HasMaxLength(64);
            e.Property(x => x.Icao).HasMaxLength(16);
            e.HasOne<Career>().WithMany().HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<AirportRow>(e =>
        {
            e.HasKey(x => x.Ident);
            e.Property(x => x.Ident).HasMaxLength(16);
            e.HasIndex(x => new { x.Latitude, x.Longitude });
        });

        m.Entity<AirportFacilityCacheRow>(e =>
        {
            e.HasKey(x => x.Ident);
            e.Property(x => x.Ident).HasMaxLength(16);
        });
    }

    /// <summary>The SKYCAREER_DB environment variable points the app at another database (testing, development).</summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("SKYCAREER_DB") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyCareer", "skycareer.db");

    public static DbContextOptions<SkyCareerDb> Options(string path) =>
        new DbContextOptionsBuilder<SkyCareerDb>().UseSqlite($"Data Source={path}").Options;
}

/// <summary>Used by `dotnet ef` to create migrations.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SkyCareerDb>
{
    public SkyCareerDb CreateDbContext(string[] args) => new(SkyCareerDb.Options("design.db"));
}
