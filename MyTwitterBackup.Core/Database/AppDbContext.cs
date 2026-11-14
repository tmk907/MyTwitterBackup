using Microsoft.EntityFrameworkCore;

namespace MyTwitterBackup.Core.Database;

public class AppDbContext(string databasePath) : DbContext
{
    public const string DatabaseFileName = "MyTwitterBackup.db";

    public string DatabasePath { get; } = databasePath;

    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<PostSource> PostSources => Set<PostSource>();
    public DbSet<QueueItem> QueueItems => Set<QueueItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Url);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.QuotedPost)
                .WithMany()
                .HasForeignKey(x => x.QuotedPostId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Media)
                .WithOne()
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Sources)
                .WithOne()
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PostMedia>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.MediaId }).IsUnique();
            e.Property(x => x.Type).HasConversion<string>();
        });

        modelBuilder.Entity<PostSource>(e =>
        {
            e.HasKey(x => new { x.PostId, x.SourceType });
            e.HasIndex(x => x.SourceType);
        });

        modelBuilder.Entity<QueueItem>(e =>
        {
            e.Ignore(x => x.Url);
            e.HasIndex(x => new { x.PostId, x.SourceType }).IsUnique();
            e.HasIndex(x => x.Status);
        });
    }
}
