# CloudCord Setup

CloudCord is a project that provides a set of APIs for managing files. It allows you to upload, download, and delete files. It also supports chunked file upload.

### Speeds

Up: ~ 4,5 MB/s
Down: ~ 7,5 MB/s

## Setup Instructions

### Prerequisites

- .NET 8.0 or higher
- IDE that supports .NET development

### Clone the Repository

First, clone the repository to your local machine using the following command:

```bash
git clone https://github.com/SymoHTL/CloudCord.git
```

### Configure `appsettings.json`

Navigate to the `CloudCord` directory and open the `appsettings.json` file. This file contains various configuration options for the application.

Here is a brief explanation of each configuration option:

- `Logging`: This section is used to configure the logging level.
- `AllowedHosts`: This option is used to specify which hosts are allowed to connect to the application.
- `ConnectionStrings`: This section is used to configure the database connection string.
- `KeyPath`: Path to the DataProtection Keys of this app, if run in a docker environment they must be stored in a persistent volume.
- `Tokens`: List of DiscordBot tokens which are used to upload/download your files, using multiple bots can prevent ratelimiting if you have heavy usage.
- `GuildId`: Id of your Discord server, (Settings -> Advanced -> Developer Mode -> rightclick on your server -> Copy Server ID).
- `ChannelId`: Channel where your files will be stored.

Please replace the placeholders with your actual data.

Setup discord bot:
https://discordpy.readthedocs.io/en/stable/discord.html

You need to enable the Message Content Intent

![image](https://github.com/SymoHTL/CloudCord/assets/54981573/3727680b-df95-4473-b1c0-e3211bee9f42)

### Build and Run the Project

Open the project in your IDE. Build and run the project.

# CloudCord

CloudCord is a project that provides a set of APIs for managing files. It allows you to upload, download, and delete files. It also supports chunked file upload.

## How to Use

### Upload a File

To upload a file, make a POST request to the `/api/files` endpoint. The file to be uploaded should be included in the request body as form data.

```bash
curl -X POST -H "Content-Type: multipart/form-data" -F "file=@path_to_your_file" http://localhost:5000/api/files
```

### Upload a File in Chunks

To upload a file in chunks, make a POST request to the `/api/files/chunked` endpoint. The chunk of the file to be uploaded should be included in the request body as form data. You also need to provide the start byte of the chunk.

```bash
curl -X POST -H "Content-Type: multipart/form-data" -F "file=@path_to_your_chunk" -F "startByte=0" http://localhost:5000/api/files/chunked
```

### Download a File

To download a file, make a GET request to the `/api/files/{fileId}` endpoint. Replace `{fileId}` with the ID of the file you want to download.

```bash
curl -X GET http://localhost:5000/api/files/{fileId}
```

### Delete a File

To delete a file, make a DELETE request to the `/api/files/{fileId}` endpoint. Replace `{fileId}` with the ID of the file you want to delete.

```bash
curl -X DELETE http://localhost:5000/api/files/{fileId}
```

## Running the Project Locally

1. Clone the repository to your local machine.
2. Open the project in your IDE.
3. Build and run the project.

Please note that you need to have .NET 8.0 or higher installed on your machine to run this project.

# CloudCordClient

CloudCordClient is a C# library for interacting with the CloudCord backend. It provides functionalities for uploading, downloading, and deleting files.

## Usage

### Initialization

First, you need to initialize the `CloudCordService`:

```csharp
IHttpClientFactory factory = ...; // Get your IHttpClientFactory
ILogger<CloudCordService> logger = ...; // Get your ILogger
IOptions<CloudCordClientSettings> settings = ...; // Get your IOptions

CloudCordService service = new CloudCordService(factory, logger, settings);
```

or with Dependency Injection:
Here's how you can set up the `CloudCordService` in an ASP.NET Core application:

```csharp
builder.Services.AddHttpClient("CloudCord", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["CloudCord:BaseAddress"]);
});

builder.Services.Configure<CloudCordClientSettings>(builder.Configuration.GetSection("CloudCord"));

builder.Services.AddScoped<CloudCordService>();
```

In the `appsettings.json` file, you would have something like:

```json
{
  "CloudCord": {
    "BaseAddress": "https://your-cloudcord-api-url",
    "ChunkSize": 26214400
  }
}
```

In this setup, the `CloudCordService` will be properly initialized with the `IHttpClientFactory`, `ILogger<CloudCordService>`, and `IOptions<CloudCordClientSettings>` instances whenever it's injected into a controller or another service.

### Uploading a File

To upload a file, you can use the `Upload` method:

```csharp
Stream stream = ...; // Your file stream
string downloadFileName = ...; // The name of the file to upload
CancellationToken ct = ...; // Your cancellation token

string fileId = await service.Upload(stream, downloadFileName, ct);
```

### Uploading a File in Chunks

To upload a file in chunks, you can use the `UploadChunked` method:

```csharp
Stream stream = ...; // Your file stream
string downloadFileName = ...; // The name of the file to upload
CancellationToken ct = ...; // Your cancellation token
Func<string, long, Task> onChunkUploaded = ...; // Your callback function

string fileId = await service.UploadChunked(stream, downloadFileName, ct, onChunkUploaded);
```

### Downloading a File

To download a file, you can use the `Download` method:

```csharp
string fileId = ...; // The ID of the file to download
CancellationToken ct = ...; // Your cancellation token

Stream fileStream = await service.Download(fileId, ct);
```

### Deleting a File

To delete a file, you can use the `Delete` method:

```csharp
string fileId = ...; // The ID of the file to delete
CancellationToken ct = ...; // Your cancellation token

await service.Delete(fileId, ct);
```

## Exceptions

The library can throw `UploadChunkException` if there is an error during the chunk upload process.
It also throws any Http Error that occurs



## Contributing

We welcome contributions! Please create a new branch for your feature or bug fix, then submit a pull request.
