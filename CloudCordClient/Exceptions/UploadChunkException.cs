namespace CloudCordClient.Exceptions;

public class UploadChunkException(string failedToUploadChunk) : Exception(failedToUploadChunk);