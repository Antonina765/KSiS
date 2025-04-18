namespace FileStorage.DTO;

public class HeadFileDTO
{
    public bool Exists { get; set; }
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}