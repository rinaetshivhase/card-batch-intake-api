using CardBatchIntakeAll.Models;
using Microsoft.EntityFrameworkCore;

namespace CardBatchIntakeAll.Data
{
     // EF Core database context
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Batch> Batches => Set<Batch>();  // Batches table
        public DbSet<CardRecord> CardRecords => Set<CardRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.FileName)
                .IsUnique();

            modelBuilder.Entity<Batch>()
                .Property(b => b.Status)
                .HasConversion<string>();
        }
    }
}