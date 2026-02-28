using VideoArchive.Models;

namespace VideoArchive.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetAllAsync();
    Task<Tag> CreateAsync(string name, string? color = null);
    Task DeleteAsync(int id);
    Task AddTagToVideoAsync(int videoId, int tagId);
    Task RemoveTagFromVideoAsync(int videoId, int tagId);
}
