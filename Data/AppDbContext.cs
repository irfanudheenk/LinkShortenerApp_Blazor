using LinkShortenerApp.Models;
using Microsoft.EntityFrameworkCore;

namespace LinkShortenerApp.Data
{
    /// <summary>
    /// Entity Framework Core Database Context
    /// This class represents the session with the database and provides access to our data
    /// It acts as a bridge between our application and the database tables
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Constructor that accepts DbContextOptions
        /// These options are configured in Program.cs and contain the database connection string
        /// </summary>
        /// <param name="options">Configuration options including connection string</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        /// <summary>
        /// DbSet represents a table in our database
        /// Each property here becomes a database table
        /// ShortUrls will map to a table named "ShortUrls" with columns matching the ShortUrl model
        /// </summary>
        public DbSet<ShortUrl> ShortUrls { get; set; }

        /// <summary>
        /// Configure model relationships and constraints
        /// This method is called by EF Core when creating the database schema
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure the ShortUrl entity with additional constraints
            modelBuilder.Entity<ShortUrl>(entity =>
            {
                // Set ShortCode as required with a maximum length of 10 characters
                // This ensures codes aren't too long and provides index optimization
                entity.Property(e => e.ShortCode)
                    .IsRequired()
                    .HasMaxLength(10);

                // Set OriginalUrl as required with a maximum length of 2048
                // 2048 is the maximum URL length supported by most browsers
                entity.Property(e => e.OriginalUrl)
                    .IsRequired()
                    .HasMaxLength(2048);

                // Create a unique index on ShortCode for faster lookups
                // Since we query by ShortCode frequently, indexing improves performance
                entity.HasIndex(e => e.ShortCode)
                    .IsUnique();

                // Add default value for CreatedDate
                // This ensures the database sets the date if the application doesn't
                entity.Property(e => e.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()");

                // Add default value for ClickCount
                entity.Property(e => e.ClickCount)
                    .HasDefaultValue(0);
            });
        }
    }
}