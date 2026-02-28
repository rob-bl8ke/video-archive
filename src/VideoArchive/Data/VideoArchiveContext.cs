using Microsoft.EntityFrameworkCore;
using VideoArchive.Models;

namespace VideoArchive.Data;

public class VideoArchiveContext : DbContext
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<VideoTag> VideoTags => Set<VideoTag>();
    public DbSet<VideoSegment> VideoSegments => Set<VideoSegment>();
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // TODO: Wire up in Phase 2.2
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO: Configure relationships in Phase 2.2
    }
}
