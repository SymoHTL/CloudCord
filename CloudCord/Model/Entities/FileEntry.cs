namespace CloudCord.Model.Entities;

[Table("Files")]
public class FileEntry : IEntityTypeConfiguration<FileEntry> {
    [Key] public ulong MessageId { get; set; }

    [Required] [StringLength(70)] public string FileName { get; set; } = null!;

    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long Size { get; set; }

    public void Configure(EntityTypeBuilder<FileEntry> builder) { }
}