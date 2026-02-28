using Microsoft.EntityFrameworkCore;
using VideoArchive.Data;
using VideoArchive.Models;

namespace VideoArchive.Services;

public class TagService(VideoArchiveContext context) : ITagService
{
    public async Task<IReadOnlyList<Tag>> GetAllAsync()
        => await context.Tags.OrderBy(t => t.Name).ToListAsync();

    public async Task<Tag> CreateAsync(string name, string? color = null)
    {
        var tag = new Tag { Name = name, Color = color };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }

    public async Task DeleteAsync(int id)
    {
        var tag = await context.Tags.FindAsync(id);
        if (tag is not null)
        {
            context.Tags.Remove(tag);
            await context.SaveChangesAsync();
        }
    }

    public async Task AddTagToVideoAsync(int videoId, int tagId)
    {
        var exists = await context.VideoTags.AnyAsync(vt => vt.VideoId == videoId && vt.TagId == tagId);
        if (!exists)
        {
            context.VideoTags.Add(new VideoTag { VideoId = videoId, TagId = tagId });
            await context.SaveChangesAsync();
        }
    }

    public async Task RemoveTagFromVideoAsync(int videoId, int tagId)
    {
        var vt = await context.VideoTags.FirstOrDefaultAsync(vt => vt.VideoId == videoId && vt.TagId == tagId);
        if (vt is not null)
        {
            context.VideoTags.Remove(vt);
            await context.SaveChangesAsync();
        }
    }
}
