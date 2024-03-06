using System.Diagnostics;
using System.Reflection;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Caching.Memory;

namespace CloudCord.Services;

public class DcMsgService(
    ILogger<DcMsgService> logger,
    IOptions<DiscordCfg> dcCfg,
    IServiceProvider sP) {
    private DiscordSocketClient[] Clients { get; set; } = [];
    private SemaphoreSlim Semaphore { get; } = new(1, 1);

    private IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    public async Task<SocketTextChannel?> GetChannelAsync(ulong guildId, ulong channelId, CancellationToken ct) {
        await Semaphore.WaitAsync(ct);
        try {
            var client = Clients[Random.Shared.Next(Clients.Length)];
            var guild = client.GetGuild(guildId);
            var channel = guild?.GetTextChannel(channelId);
            if (channel == null) {
                logger.LogError("Channel {Channel} not found in guild {Guild}", channelId, guildId);
                return null;
            }

            logger.LogInformation("Getting channel {Channel} from guild {Guild}", channelId, guildId);
            return channel;
        }
        finally {
            Semaphore.Release();
        }
    }

    public async Task<AttachmentMessage> GetMessageAsync(ulong id, CancellationToken ct) {
        var channel = await GetChannelAsync(dcCfg.Value.GuildId, dcCfg.Value.ChannelId, ct);

        if (channel is null) {
            logger.LogError("Channel {Channel} not found in guild {Guild}", dcCfg.Value.ChannelId, dcCfg.Value.GuildId);
            throw new InvalidOperationException("Channel not found");
        }


        if (Cache.TryGetValue<AttachmentMessage>(id, out var msg) && msg is not null) return msg;
            
        var dcMsg = await channel.GetMessageAsync(id, new RequestOptions(){CancelToken = ct});
        msg = new AttachmentMessage(dcMsg);
        Cache.Set(id, msg, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
        return msg;
    }

    public async Task DeleteMessagesAsync(IEnumerable<ulong> select, CancellationToken ct) {
        var channel = await GetChannelAsync(dcCfg.Value.GuildId, dcCfg.Value.ChannelId, ct);
        if (channel is not null) await channel.DeleteMessagesAsync(select, new RequestOptions { CancelToken = ct});
    }

    public async Task InitAsync(IEnumerable<string> tokens) {
        var tasks = new List<Task>();
        var enumerable = tokens as string[] ?? tokens.ToArray();
        Clients = new DiscordSocketClient[enumerable.Length];
        for (var i = 0; i < enumerable.Length; i++) {
            var token = enumerable[i];
            var client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.All });
            var interactionService = new InteractionService(client.Rest);
            client.Log += msg => {
                logger.Log(GetLogLevel(msg.Severity), msg.Exception, "Source: {Source}, Message: {Message}", msg.Source,
                    msg.Message);
                return Task.CompletedTask;
            };
            client.Ready += async () => {
                await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), sP);
                await interactionService.RegisterCommandsToGuildAsync(dcCfg.Value.GuildId);
            };
            client.InteractionCreated += async interaction => {
                var scope = sP.CreateScope();
                var ctx = new SocketInteractionContext(client, interaction);
                await interactionService.ExecuteCommandAsync(ctx, scope.ServiceProvider);
            };

            tasks.Add(StartClientAsync(client, token));
            Clients[i] = client;
        }

        await Task.WhenAll(tasks);
        await Task.Delay(1000);
    }

    private async Task StartClientAsync(DiscordSocketClient client, string token) {
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        await Task.Run(async () => {
            while (client.ConnectionState != ConnectionState.Connected) await Task.Delay(1);
        });
    }

    private static LogLevel GetLogLevel(LogSeverity severity) {
        return severity switch {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.None
        };
    }
}