using Microsoft.EntityFrameworkCore;
using VideoArchive.Data;
using VideoArchive.Models;

namespace VideoArchive.Services;

public class VideoRepository(VideoArchiveContext context) : IVideoRepository
{
    public async Task<IReadOnlyList<Video>> GetAllAsync()
        => await context.Videos.Include(v => v.VideoTags).ThenInclude(vt => vt.Tag).ToListAsync();

    public async Task<Video?> GetByIdAsync(int id)
        => await context.Videos.Include(v => v.VideoTags).ThenInclude(vt => vt.Tag).FirstOrDefaultAsync(v => v.Id == id);

    public async Task<Video?> GetByFilePathAsync(string filePath)
        => await context.Videos.FirstOrDefaultAsync(v => v.FilePath == filePath);

    public async Task AddAsync(Video video)
    {
        context.Videos.Add(video);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Video video)
    {
        context.Videos.Update(video);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var video = await context.Videos.FindAsync(id);
        if (video is not null)
        {
            context.Videos.Remove(video);
            await context.SaveChangesAsync();
        }
    }
}
