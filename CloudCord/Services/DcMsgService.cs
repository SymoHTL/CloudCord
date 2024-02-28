using System.Reflection;
using Discord;
using Discord.Interactions;

namespace CloudCord.Services;

public class DcMsgService(ILogger<DcMsgService> logger, IOptions<DiscordCfg> dcCfg, IServiceProvider sP) {
    private List<DiscordSocketClient> Clients { get; } = [];
    private SemaphoreSlim Semaphore { get; } = new(1, 1);

    public async Task<SocketTextChannel?> GetChannel(ulong guildId, ulong channelId) {
        await Semaphore.WaitAsync();
        try {
            var client = Clients[Random.Shared.Next(Clients.Count)];
            var guild = client.GetGuild(guildId);
            var channel = guild?.GetTextChannel(channelId);
            if (channel == null) {
                logger.LogError("Channel {Channel} not found in guild {Guild} - {SocketClient}", channelId, guildId,
                    client.CurrentUser.Username);
                return null;
            }

            logger.LogInformation("Getting channel {Channel} from guild {Guild} - {SocketClient}", channel.Name,
                guild?.Name, client.CurrentUser.Username);
            return channel;
        }
        finally {
            Semaphore.Release();
        }
    }

    public async Task InitAsync(IEnumerable<string> tokens) {
        var tasks = new List<Task>();
        foreach (var token in tokens) {
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
            Clients.Add(client);
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