using System.Text;

namespace CloudCord.Controllers;

[ApiController]
[Route("api/files")]
public class FileController(
    DcMsgService dcMsgService,
    Repository<FileEntry> repository,
    IOptions<DiscordCfg> cfg,
    IHttpClientFactory factory,
    ILogger<FileController> logger)
    : ControllerBase {
    private const long MaxDcChunkSize = 25 * 1024 * 1024; // 25MB

    private const long MaxFileSize = 10L * 1024 * 1024 * 1024; // 10GB

    private readonly HttpClient _httpClient = factory.CreateClient("default");

    private string RandomFileName(int length = 64) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[RandomNumberGenerator.GetInt32(s.Length - 1)]).ToArray());
    }


    [HttpGet("{fileId}")]
    public async Task Stream(string fileId, CancellationToken ct) {
        var files = await repository.ReadAsync(f => f.FileId == fileId, f => f.StartByte, ct);
        if (files.Count == 0) {
            await RespondNotFound("File not found", ct);
            return;
        }

        var msg = await dcMsgService.GetMessageAsync(files.First().MessageId, ct);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
            FileName = msg.FileName
        }.ToString();

        var rangeHeader = Request.Headers.Range;
        if (!string.IsNullOrEmpty(rangeHeader)) await ProcessRangeRequest(files, rangeHeader, Response.Body, ct);
        else await ProcessFullFileRequest(files, Response.Body,false, ct);
    }

    [HttpGet("{fileId}/{rawKey}")]
    public async Task StreamSecure(string fileId, string rawKey, CancellationToken ct) {
        var files = await repository.ReadAsync(f => f.FileId == fileId, f => f.StartByte, ct);
        if (files.Count == 0) {
            await RespondNotFound("File not found", ct);
            return;
        }

        var msg = await dcMsgService.GetMessageAsync(files.First().MessageId, ct);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
            FileName = msg.FileName
        }.ToString();

        var transform = Aes.Create();
        var key = SHA512.HashData(Encoding.UTF8.GetBytes(rawKey));
        transform.Key = key.Take(32).ToArray();
        transform.IV = key.Skip(32).Take(16).ToArray();

        await using var cryptoStream =
            new CryptoStream(Response.Body, transform.CreateDecryptor(), CryptoStreamMode.Write);

        var rangeHeader = Request.Headers.Range;
        if (!string.IsNullOrEmpty(rangeHeader)) await ProcessRangeRequest(files, rangeHeader, cryptoStream, ct);
        else await ProcessFullFileRequest(files, cryptoStream, true, ct);
    }


    [RequestSizeLimit(MaxFileSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    [HttpPost]
    public async Task<ActionResult<string>> Upload([Required] IFormFile file, CancellationToken ct) {
        var fileName = RandomFileName();

        var success = await Upload(file, fileName, ct);
        if (!success) return BadRequest("Failed to upload to discord");

        return Ok(fileName);
    }

    [RequestSizeLimit(MaxFileSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    [HttpPost("{rawKey}")]
    public async Task<ActionResult<string>> UploadSecure([Required] IFormFile file, [FromRoute] string rawKey,
        CancellationToken ct) {
        var fileName = RandomFileName();

        var transform = Aes.Create();
        var key = SHA512.HashData(Encoding.UTF8.GetBytes(rawKey));
        transform.Key = key.Take(32).ToArray();
        transform.IV = key.Skip(32).Take(16).ToArray();

        await using var cryptoStream =
            new CryptoStream(file.OpenReadStream(), transform.CreateEncryptor(), CryptoStreamMode.Read);

        var success = await Upload(cryptoStream, fileName, file.FileName, ct);
        if (!success) return BadRequest("Failed to upload to discord");

        return Ok(fileName);
    }

    [RequestSizeLimit(MaxFileSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    [HttpPost("chunked")]
    public async Task<ActionResult<ReadChunkDto>> UploadChunk([Required] IFormFile chunkFile, [FromForm] string? fileId,
        [Required] [FromForm] long startByte, CancellationToken ct) {
        if (fileId is null) {
            fileId = RandomFileName();
        }
        else {
            var files = await repository.ReadAsync(f => f.FileId == fileId, ct);
            if (files.Count == 0) return BadRequest("File does not exist");
        }

        var success = await Upload(chunkFile, fileId, ct, startByte);
        if (!success) return BadRequest("Failed to upload to discord");

        return Ok(new ReadChunkDto {
            FileId = fileId,
            StartByte = startByte,
            EndByte = startByte + chunkFile.Length
        });
    }

    [HttpDelete("{fileId}")]
    public async Task<IActionResult> Delete(string fileId, CancellationToken ct) {
        var files = await repository.ReadAsync(f => f.FileId == fileId, f => f.StartByte, ct);

        logger.LogInformation("Deleting {FileId} from discord", fileId);

        await dcMsgService.DeleteMessagesAsync(files.Select(f => f.MessageId), ct);

        await repository.DeleteAsync(files, ct);
        return Ok();
    }

    private async Task<bool> Upload(IFormFile file, string fileId, CancellationToken ct, long startByte = 0) {
        await using var stream = file.OpenReadStream();
        return await Upload(stream, fileId, file.FileName, ct, startByte);
    }

    private async Task<bool> Upload(Stream stream, string fileId, string downloadFileName, CancellationToken ct,
        long startByte = 0) {
        var channel = await dcMsgService.GetChannelAsync(cfg.Value.GuildId, cfg.Value.ChannelId, ct);
        if (channel is null) return false;
        logger.LogInformation("Uploading file {FileId} to discord - {ChannelId}", fileId, channel.Id);

        var buffer = new byte[MaxDcChunkSize];
        var endByte = startByte;
        int bytesRead;
        var messages = new List<FileEntry>();

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0) {
            var ms = new MemoryStream(buffer, 0, bytesRead);
            var msg = await channel.SendFileAsync(ms, downloadFileName);
            await ms.DisposeAsync();
            if (msg is null) return false;
            startByte = endByte;
            endByte += bytesRead;
            messages.Add(new FileEntry {
                FileId = fileId,
                Size = bytesRead,
                StartByte = startByte,
                EndByte = endByte,
                MessageId = msg.Id
            });
        }

        await repository.CreateAsync(messages, ct);
        return true;
    }


    private static (long start, long end) ParseRangeHeader(string rangeHeader, long totalLength) {
        var range = RangeHeaderValue.Parse(rangeHeader);
        var r = range.Ranges.First();

        var start = r.From ?? 0;
        var end = r.To ?? totalLength - 1;

        if (end >= totalLength) end = totalLength - 1;

        return (start, end);
    }

    private async Task StreamFilesInto(Stream output, List<FileEntry> files,
        CancellationToken ct) {
        foreach (var entry in files) {
            var msg = await dcMsgService.GetMessageAsync(entry.MessageId, ct);
            var dataStream = await _httpClient.GetStreamAsync(msg.Url, ct);
            await dataStream.CopyToAsync(output, ct);
            await dataStream.DisposeAsync(); // Manually dispose to immediately release resources
        }
    }

    private async Task ProcessFullFileRequest(List<FileEntry> files, Stream output, bool noContentLength, CancellationToken ct) {
        Response.StatusCode = 200;
        if (!noContentLength) Response.Headers.Append("Content-Length", files.Last().EndByte.ToString());
        await StreamFilesInto(output, files, ct);
    }

    private async Task ProcessRangeRequest(List<FileEntry> files, StringValues rangeHeader, Stream output,
        CancellationToken ct) {
        var totalSize = files.Last().EndByte;
        var (start, end) = ParseRangeHeader(rangeHeader!, totalSize);
        files = files.Where(file => file.StartByte <= end && file.EndByte >= start).ToList();

        if (files.Count == 0) {
            Response.StatusCode = 416; // Requested Range Not Satisfiable
            return;
        }

        Response.StatusCode = 206; // Partial content
        var contentLength = end - start + 1;
        Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{files.Last().EndByte}");
        Response.Headers.Append("Content-Length", contentLength.ToString());
        Response.ContentType = "application/octet-stream";

        foreach (var entry in files) {
            var adjustedStart = Math.Max(start, entry.StartByte) - entry.StartByte;
            var adjustedEnd = Math.Min(end, entry.EndByte) - entry.StartByte;

            var msg = await dcMsgService.GetMessageAsync(entry.MessageId, ct);
            await ProcessRangeRequest(adjustedStart, adjustedEnd, msg.Url, output, ct);
        }
    }

    private async Task ProcessRangeRequest(long startByte, long endByte, string url, Stream output,
        CancellationToken ct) {
        var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Range = new RangeHeaderValue(startByte, endByte);
        var response = await _httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(output, ct);
        response.Dispose();
    }

    private async Task RespondNotFound(string message, CancellationToken ct) {
        Response.StatusCode = 404;
        await Response.WriteAsync(message, ct);
    }
}