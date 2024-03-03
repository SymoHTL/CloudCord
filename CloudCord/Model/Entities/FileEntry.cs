namespace CloudCord.Model.Entities;

[Table("Files")]
public class FileEntry : IEntityTypeConfiguration<FileEntry> {
    [Key] public ulong MessageId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 60)]
    public string FileId { get; set; } = null!;

    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long Size { get; set; }

    public void Configure(EntityTypeBuilder<FileEntry> builder) { }
}