using Microsoft.EntityFrameworkCore;
using VideoArchive.Models;

namespace VideoArchive.Data;

public class VideoArchiveContext : DbContext
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VideoArchive",
        "videoarchive.db");

    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<VideoTag> VideoTags => Set<VideoTag>();
    public DbSet<VideoSegment> VideoSegments => Set<VideoSegment>();
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        optionsBuilder.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // VideoTag composite key (junction table)
        modelBuilder.Entity<VideoTag>()
            .HasKey(vt => new { vt.VideoId, vt.TagId });

        modelBuilder.Entity<VideoTag>()
            .HasOne(vt => vt.Video)
            .WithMany(v => v.VideoTags)
            .HasForeignKey(vt => vt.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VideoTag>()
            .HasOne(vt => vt.Tag)
            .WithMany(t => t.VideoTags)
            .HasForeignKey(vt => vt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // VideoSegment → Video
        modelBuilder.Entity<VideoSegment>()
            .HasOne(s => s.Video)
            .WithMany(v => v.Segments)
            .HasForeignKey(s => s.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        modelBuilder.Entity<Video>()
            .HasIndex(v => v.FilePath)
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<LibraryFolder>()
            .HasIndex(f => f.Path)
            .IsUnique();
    }
}
