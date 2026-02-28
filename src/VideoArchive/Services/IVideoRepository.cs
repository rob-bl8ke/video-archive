using VideoArchive.Models;

namespace VideoArchive.Services;

public interface IVideoRepository
{
    Task<IReadOnlyList<Video>> GetAllAsync();
    Task<Video?> GetByIdAsync(int id);
    Task<Video?> GetByFilePathAsync(string filePath);
    Task AddAsync(Video video);
    Task UpdateAsync(Video video);
    Task DeleteAsync(int id);
}
