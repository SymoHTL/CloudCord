namespace UnitTests;

public class Tests {
    private const long FileLength = 100 * 1024 * 1024; // 100MB
    private CloudCordService _cordService = null!;

    [SetUp]
    public void Setup() {
        var httpFactory = new ClientFactory();
        httpFactory.AddClient("CloudCord", new HttpClient { BaseAddress = new Uri("http://localhost:5299") });
        var loggerFactory = new LoggerFactory();
        var logger = loggerFactory.CreateLogger<CloudCordService>();

        var options = new CloudCordClientSettings {
            ChunkSize = 5 * 1024 * 1024
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
    public async Task UploadSampleVideo() {
        var stream = File.OpenRead("sample.mp4");
        var start = DateTime.Now;
        var fileId = await _cordService.Upload(stream, "sample.mp4", CancellationToken.None);
        Assert.That(fileId, Is.Not.Null);
        Console.WriteLine(fileId);
        var mbps = stream.Length / (DateTime.Now - start).TotalSeconds / 1024 / 1024;
        Console.WriteLine($"Speed: {mbps} MB/s");
    }

    [Test]
    public async Task UploadSampleVideoChunked() {
        var stream = File.OpenRead("sample.mp4");
        var start = DateTime.Now;
        var fileId = await _cordService.UploadChunked(stream, "sample.mp4", CancellationToken.None, (id, end) => {
            Console.WriteLine($"Uploaded chunk {id} to {end}");
            return Task.CompletedTask;
        });
        Assert.That(fileId, Is.Not.Null);
        Console.WriteLine(fileId);
        var mbps = stream.Length / (DateTime.Now - start).TotalSeconds / 1024 / 1024;
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
        const string fileId = "H1Hky0eOzksIaqEhk6hBjgbWJMoIjXasbdP8TiBQG5HrcgDVgVaecdAkWh1dehrv";
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
        const string fileId = "p6DeCjNAO8cBOmmDObaMDliaWawLWzLi4IObXe4kSwY5utjXoBBJLnljTiAgGCe2";
        await _cordService.Delete(fileId, CancellationToken.None);
    }

    [Test]
    public async Task DownloadRanged() {
        const string fileId = "H1Hky0eOzksIaqEhk6hBjgbWJMoIjXasbdP8TiBQG5HrcgDVgVaecdAkWh1dehrv";
        var start = DateTime.Now;
        var stream = await _cordService.Download(fileId, 11, 110, CancellationToken.None);

        ulong byteAmount = 0;
        while (stream.ReadByte() != -1) byteAmount++;

        Assert.That(byteAmount, Is.EqualTo(100));
    }

    [Test]
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public async Task BenchmarkDownload(int amount) {
        const string fileId = "H1Hky0eOzksIaqEhk6hBjgbWJMoIjXasbdP8TiBQG5HrcgDVgVaecdAkWh1dehrv";
        var tasks = new List<Task<ulong>>();
        var start = DateTime.Now;
        for (var i = 0; i < amount; i++) tasks.Add(Download(fileId));
        var results = await Task.WhenAll(tasks);
        var sum = results.Aggregate<ulong, ulong>(0, (current, r) => current + r);
        Console.WriteLine("Total mb: " + sum / 1024 / 1024);
        Console.WriteLine("Total time: " + (DateTime.Now - start).TotalSeconds);
    }

    [Test]
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public async Task BenchmarkRangedDownload(int amount) {
        const string fileId = "H1Hky0eOzksIaqEhk6hBjgbWJMoIjXasbdP8TiBQG5HrcgDVgVaecdAkWh1dehrv";
        var tasks = new List<Task<ulong>>();
        var start = DateTime.Now;
        for (var i = 0; i < amount; i++) tasks.Add(DownloadRanged(fileId));
        var results = await Task.WhenAll(tasks);
        var sum = results.Aggregate<ulong, ulong>(0, (current, r) => current + r);
        Console.WriteLine("Total mb: " + sum / 1024 / 1024);
        Console.WriteLine("Total time: " + (DateTime.Now - start).TotalSeconds);
    }

    private async Task<ulong> Download(string fileId) {
        var start = DateTime.Now;
        var data = await _cordService.Download(fileId, CancellationToken.None);
        ulong byteAmount = 0;
        while (data.ReadByte() != -1) byteAmount++;
        Console.WriteLine("completed - " + (DateTime.Now - start).TotalSeconds);
        return byteAmount;
    }

    private const ulong BenchmarkFileSize = 17839845;

    private async Task<ulong> DownloadRanged(string fileId) {
        var start = DateTime.Now;
        var data = await _cordService.Download(fileId, 0, BenchmarkFileSize / 2, CancellationToken.None);
        ulong byteAmount = 0;
        while (data.ReadByte() != -1) byteAmount++;
        Console.WriteLine("completed - " + (DateTime.Now - start).TotalSeconds);
        return byteAmount;
    }
}