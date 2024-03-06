using System.Text;
using CloudCordClient.Entities;
using CloudCordClient.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class Tests {
    private CloudCordService _cordService = null!;

    private const long FileLength = 100 * 1024 * 1024; // 100MB

    [SetUp]
    public void Setup() {
        var httpFactory = new ClientFactory();
        httpFactory.AddClient("CloudCord", new HttpClient { BaseAddress = new Uri("http://localhost:5299") });
        var loggerFactory = new LoggerFactory();
        var logger = loggerFactory.CreateLogger<CloudCordService>();

        var options = new CloudCordClientSettings {
            ChunkSize = 20 * 1024 * 1024
        };

        _cordService = new CloudCordService(httpFactory, logger, Options.Create(options));
    }

    public Stream CreateFile() {
        var sb = new StringBuilder();
        for (var i = 0; i < FileLength; i++) sb.Append('a');

        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    [Test]
    public async Task Upload() {
        var stream = CreateFile();
        var start = DateTime.Now;
        var fileId = await _cordService.Upload(stream, "hello.txt", CancellationToken.None);
        Assert.That(fileId, Is.Not.Null);
        Console.WriteLine(fileId);
        var mbps = FileLength / (DateTime.Now - start).TotalSeconds / 1024 / 1024;
        Console.WriteLine($"Speed: {mbps} MB/s");
    }

    [Test]
    public async Task UploadChunked() {
        var stream = CreateFile();
        var start = DateTime.Now;
        var fileId = await _cordService.UploadChunked(stream, "hello.txt", CancellationToken.None, (id, end) => {
            Console.WriteLine($"Uploaded chunk {id} to {end}");
            return Task.CompletedTask;
        });

        Assert.That(fileId, Is.Not.Null);
        Console.WriteLine(fileId);
        var mbps = FileLength / (DateTime.Now - start).TotalSeconds / 1024 / 1024;
        Console.WriteLine($"Speed: {mbps} MB/s");
    }

    [Test]
    public async Task Download() {
        const string fileId = "7ixdF7WlgkSvUaAzgeNkgjPUjGZwMNCZwDxjSYh24666jHgDoS6h1pWrENgWbEdW";
        var start = DateTime.Now;
        var stream = await _cordService.Download(fileId, CancellationToken.None);
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var shouldBe = new string('a', (int)FileLength);
        Assert.That(content, Is.EqualTo(shouldBe));
        var mbps = FileLength / (DateTime.Now - start).TotalSeconds / 1024 / 1024;
        Console.WriteLine($"Speed: {mbps} MB/s");
    }

    [Test]
    public async Task Delete() {
        const string fileId = "7ixdF7WlgkSvUaAzgeNkgjPUjGZwMNCZwDxjSYh24666jHgDoS6h1pWrENgWbEdW";
        await _cordService.Delete(fileId, CancellationToken.None);
    }
}