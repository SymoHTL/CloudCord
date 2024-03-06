namespace Shared.DTO;

public class ReadChunkDto {
    public required string FileId { get; set; }

    public required long StartByte { get; set; }

    public required long EndByte { get; set; }

    [JsonIgnore] public long Size => EndByte - StartByte;
}