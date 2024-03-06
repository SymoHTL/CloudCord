namespace CloudCordClient.Entities;

public class CloudCordClientSettings {
    /// <summary>
    ///     Should be a multiple of 1024 <br />
    ///     And a multiple of 25MB  is recommended because of discord's file size limit <br />
    ///     eg 25MB = 25 * 1024 * 1024
    /// </summary>
    public long ChunkSize { get; set; }
}