using System.Runtime.InteropServices;
using Fsp;
using FileInfo = Fsp.Interop.FileInfo;

namespace WinFspIntegration;

public class WinFspService : FileSystemBase {
    private readonly HttpClient _httpClient;

    public WinFspService(string baseAddress) {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public override int Read(object FileNode, object FileDesc, IntPtr Buffer, ulong Offset, uint Length,
        out uint BytesTransferred) {
        var fileName = FileNode as string;
        var response = _httpClient.GetAsync($"/files/{fileName}/read?offset={Offset}&length={Length}").Result;

        if (!response.IsSuccessStatusCode) {
            BytesTransferred = 0;
            return -1; // Proper error handling should be implemented
        }

        var bytesRead = response.Content.ReadAsByteArrayAsync().Result;

        Marshal.Copy(bytesRead, 0, Buffer, bytesRead.Length);
        BytesTransferred = (uint)bytesRead.Length;

        return 0; // STATUS_SUCCESS
    }

    public override int Write(object FileNode, object FileDesc, IntPtr Buffer, ulong Offset, uint Length,
        bool WriteToEndOfFile, bool ConstrainedIo, out uint BytesTransferred, out FileInfo fileInfo) {
        fileInfo = new FileInfo();

        var fileName = FileNode as string;
        var bytes = new byte[Length];
        Marshal.Copy(Buffer, bytes, 0, (int)Length);

        var response = _httpClient.PostAsync($"/files/{fileName}/write?offset={Offset}&length={Length}",
            new ByteArrayContent(bytes)).Result;

        if (!response.IsSuccessStatusCode) {
            BytesTransferred = 0;
            return -1; // Proper error handling should be implemented
        }

        BytesTransferred = Length;
        return 0; // STATUS_SUCCESS
    }
}