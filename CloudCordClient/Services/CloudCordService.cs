﻿using CloudCordClient.Exceptions;

namespace CloudCordClient.Services;

public class CloudCordService(
    IHttpClientFactory factory,
    ILogger<CloudCordService> logger,
    IOptions<CloudCordClientSettings> settings) {
    private readonly HttpClient _backend = factory.CreateClient("CloudCord");

    private readonly long _chunkSize = settings.Value.ChunkSize;

    /// <summary>
    /// Uploads a file to the cloudcord backend <br/>
    /// Does not close the stream <br/>
    /// Produces exceptions if the request fails
    /// </summary>
    /// <param name="stream">Stream to upload</param>
    /// <param name="downloadFileName">Name of the file to upload</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>FileId of the uploaded file</returns>
    public async Task<string> Upload(Stream stream, string downloadFileName, CancellationToken ct) {
        var content = new MultipartFormDataContent {
            { new StreamContent(stream), "file", downloadFileName }
        };

        logger.LogInformation("Uploading {FileName}", downloadFileName);

        var response = await _backend.PostAsync("api/files", content, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Uploaded {FileName}", downloadFileName);

        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Uploads a file to the cloudcord backend in chunks <br/>
    /// Does not close the stream <br/>
    /// Produces exceptions if the request fails
    /// </summary>
    /// <param name="stream">Stream to upload</param>
    /// <param name="downloadFileName">Name of the file to upload</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="onChunkUploaded">Callback for when a chunk is uploaded</param>
    /// <returns>FileId of the uploaded file</returns>
    public async Task<string> UploadChunked(Stream stream, string downloadFileName, CancellationToken ct,
        Func<string, long, Task> onChunkUploaded) {
        Memory<byte> buffer = new byte[_chunkSize];
        int bytesRead;
        ReadChunkDto? uploadedChunk = null;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0) {
            var chunk = buffer[..bytesRead];
            uploadedChunk = await UploadChunk(chunk, downloadFileName, uploadedChunk?.FileId, ct);
            if (uploadedChunk is null) throw new UploadChunkException("Failed to upload chunk");
            await onChunkUploaded(uploadedChunk.FileId, uploadedChunk.EndByte);
            
            if (bytesRead < _chunkSize) break;
        }

        if (uploadedChunk is null)
            throw new UploadChunkException("File had 0 bytes - maybe- idk");
        
        return uploadedChunk.FileId;
    }
    
    /// <summary>
    ///  Downloads a file from the cloudcord backend
    /// Produces exceptions if the request fails
    /// </summary>
    /// <param name="fileId"> FileId of the file to download</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Stream of the file</returns>
    public async Task<Stream> Download(string fileId, CancellationToken ct) {
        return await _backend.GetStreamAsync($"api/files/{fileId}", ct);
    }
    
    /// <summary>
    /// Deletes a file from the cloudcord backend
    /// Produces exceptions if the request fails
    /// </summary>
    /// <param name="fileId"> FileId of the file to delete</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task</returns>
    public async Task Delete(string fileId, CancellationToken ct) {
        var response = await _backend.DeleteAsync($"api/files/{fileId}", ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ReadChunkDto?> UploadChunk(Memory<byte> chunk, string downloadFileName, string? fileId, CancellationToken ct) {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(chunk.ToArray()), "file", downloadFileName);
        if (fileId is not null) content.Add(new StringContent(fileId), "fileId");

        var response = await _backend.PostAsync("api/files/chunked", content, ct);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<ReadChunkDto>(ct);
    }
}