namespace VideoArchive.Models;

public class LibraryFolder
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastScanned { get; set; }
}
