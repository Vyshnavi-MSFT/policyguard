using Microsoft.EntityFrameworkCore;
using PolicyGuard.Models;

namespace PolicyGuard.Data;

/// <summary>
/// Entity Framework Core database context for SQLite.
/// This is where all entities are registered and mapped to tables.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<PolicyClause> PolicyClauses => Set<PolicyClause>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Scan
        modelBuilder.Entity<Scan>()
            .HasKey(s => s.Id);
        modelBuilder.Entity<Scan>()
            .HasMany(s => s.Findings)
            .WithOne(f => f.Scan)
            .HasForeignKey(f => f.ScanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Finding
        modelBuilder.Entity<Finding>()
            .HasKey(f => f.Id);

        // Policy
        modelBuilder.Entity<Policy>()
            .HasKey(p => p.Id);
        modelBuilder.Entity<Policy>()
            .HasMany(p => p.Clauses)
            .WithOne()
            .HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        // PolicyClause
        modelBuilder.Entity<PolicyClause>()
            .HasKey(c => c.Id);
    }
}
