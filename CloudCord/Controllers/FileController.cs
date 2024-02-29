﻿namespace CloudCord.Controllers;

[ApiController]
[Route("api")]
public class FileController(
    DcMsgService dcMsgService,
    Repository<FileEntry> repository,
    IOptions<DiscordCfg> cfg,
    IHttpClientFactory factory,
    ILogger<FileController> logger)
    : ControllerBase {
    
    
    private const long MaxDcChunkSize = 25 * 1024 * 1024; // 25MB

    private readonly HttpClient _httpClient = factory.CreateClient("default");
    private readonly ILogger<FileController> _logger = logger;
    private readonly Random _random = new(DateTime.Now.Millisecond * DateTime.Now.Second);

    private string RandomFileName(int length = 64) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[_random.Next(s.Length)]).ToArray());
    }


    [HttpGet("{fileName}")]
    public async Task Stream(string fileName, CancellationToken ct) {
        var channel = await dcMsgService.GetChannel(cfg.Value.GuildId, cfg.Value.ChannelId);
        if (channel is null) {
            await RespondNotFound("Channel not found", ct);
            return;
        }

        var files = await repository.ReadAsync(f => f.FileName == fileName, f => f.StartByte, ct);
        if (files.Count == 0) {
            await RespondNotFound("File not found", ct);
            return;
        }

        var rangeHeader = Request.Headers.Range;
        if (!string.IsNullOrEmpty(rangeHeader)) await ProcessRangeRequest(channel, files, rangeHeader, ct);
        else await ProcessFullFileRequest(channel, files, ct);
    }

    [HttpPost]
    public async Task<ActionResult<string>> Upload([Required] IFormFile file, CancellationToken ct) {
        var fileName = RandomFileName();

        var success = await Upload(file, fileName, ct);
        if (!success) return BadRequest("Failed to upload to discord");

        return Ok(fileName);
    }

    [HttpDelete("{fileName}")]
    public async Task<IActionResult> Delete(string fileName, CancellationToken ct) {
        var files = await repository.ReadAsync(f => f.FileName == fileName, f => f.StartByte, ct);
        var channel = await dcMsgService.GetChannel(cfg.Value.GuildId, cfg.Value.ChannelId);
        if (channel is null) return NotFound("Channel not found");
        foreach (var file in files) {
            var msg = await channel.GetMessageAsync(file.MessageId);
            if (msg is null) continue;
            await msg.DeleteAsync();
        }
        
        await repository.DeleteAsync(files, ct);
        return Ok();
    }

    private async Task<bool> Upload(IFormFile file, string fileName, CancellationToken ct) {
        await using var stream = file.OpenReadStream();
        return await Upload(stream, file.FileName, fileName, ct);
    }

    private async Task<bool> Upload(Stream stream, string downloadFileName, string fileName, CancellationToken ct) {
        var channel = await dcMsgService.GetChannel(cfg.Value.GuildId, cfg.Value.ChannelId);
        if (channel is null) return false;

        var buffer = new byte[MaxDcChunkSize];
        var endByte = 0L;
        int bytesRead;
        var messages = new List<FileEntry>();

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0) {
            var ms = new MemoryStream(buffer, 0, bytesRead);
            var msg = await channel.SendFileAsync(ms, downloadFileName);
            await ms.DisposeAsync();
            if (msg is null) return false;
            var startByte = endByte;
            endByte += msg.Attachments.Sum(a => a.Size);
            messages.Add(new FileEntry {
                FileName = fileName,
                Size = msg.Attachments.Sum(a => a.Size),
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
        var firstRange = range.Ranges.First();

        var start = firstRange.From ?? 0;
        var end = firstRange.To ?? totalLength - 1;

        if (end >= totalLength) end = totalLength - 1;
        return (start, end);
    }

    private async Task StreamFilesInto(Stream output, SocketTextChannel channel, List<FileEntry> files,
        CancellationToken ct) {
        foreach (var entry in files) {
            var msg = await channel.GetMessageAsync(entry.MessageId);
            var dataStream = await _httpClient.GetStreamAsync(msg.Attachments.First().Url, ct);
            await dataStream.CopyToAsync(output, ct);
            await dataStream.DisposeAsync();
        }
    }

    private async Task ProcessFullFileRequest(SocketTextChannel channel, List<FileEntry> files, CancellationToken ct) {
        Response.StatusCode = 200;
        Response.Headers.Append("Content-Length", files.Last().EndByte.ToString());
        await StreamFilesInto(Response.Body, channel, files, ct);
    }

    private async Task ProcessRangeRequest(SocketTextChannel channel, List<FileEntry> files, StringValues rangeHeader,
        CancellationToken ct) {
        var (start, end) = ParseRangeHeader(rangeHeader!, files.Last().EndByte);
        files = files.Where(file => file.StartByte <= end && file.EndByte >= start).ToList();

        if (files.Count == 0) {
            Response.StatusCode = 416; // Requested Range Not Satisfiable
            return;
        }

        Response.StatusCode = 206; // Partial content
        var contentLength = end - start + 1;
        Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{files.Last().EndByte + 1}");
        Response.Headers.Append("Content-Length", contentLength.ToString());
        Response.ContentType = "application/octet-stream";

        foreach (var entry in files) {
            var msg = await channel.GetMessageAsync(entry.MessageId);
            var dataStream = await _httpClient.GetStreamAsync(msg.Attachments.First().Url, ct);

            var offset = Math.Max(start - entry.StartByte, 0);
            var length = start == 0 && end == files.Last().EndByte
                ? entry.EndByte - entry.StartByte
                : Math.Min(end - entry.StartByte, entry.EndByte - entry.StartByte) + 1;

            var buffer = new byte[MaxDcChunkSize];
            if (dataStream is not MemoryStream) {
                var ms = new MemoryStream();
                await dataStream.CopyToAsync(ms, ct);
                dataStream = ms;
            }

            dataStream.Position = offset;
            int bytesRead;
            while (length > 0 &&
                   (bytesRead =
                       await dataStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), ct)) >
                   0) {
                await Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                length -= bytesRead;
            }

            await dataStream.DisposeAsync();
        }
    }

    private async Task RespondNotFound(string message, CancellationToken ct) {
        Response.StatusCode = 404;
        await Response.WriteAsync(message, ct);
    }
}