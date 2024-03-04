using System.Collections.Concurrent;

namespace UnitTests;

public class ClientFactory : IHttpClientFactory {
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

    public HttpClient CreateClient(string name) {
        return _clients.GetOrAdd(name, new HttpClient());
    }

    public void AddClient(string name, HttpClient client) {
        _clients.TryAdd(name, client);
    }
}