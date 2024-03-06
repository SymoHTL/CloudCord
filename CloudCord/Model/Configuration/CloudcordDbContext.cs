namespace CloudCord.Model.Configuration;

public class CloudCordDbContext(DbContextOptions<CloudCordDbContext> options) : DbContext(options) {
    public DbSet<FileEntry> FileEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(CloudCordDbContext).Assembly);
    }
}