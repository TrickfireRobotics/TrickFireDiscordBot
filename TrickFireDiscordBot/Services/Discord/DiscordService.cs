using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace TrickFireDiscordBot.Services.Discord;

public class DiscordService : IHostedService, IAutoRegisteredService, IEventHandler<SessionCreatedEventArgs>
{
    /// <summary>
    /// The client associated with the bot.
    /// </summary>
    public DiscordClient Client { get; }

    public DiscordServiceOptions Options { get; }
    public DiscordGuild MainGuild { get; }


    public DiscordService(DiscordClient client, IOptions<DiscordServiceOptions> options)
    {
        Client = client;
        Options = options.Value;
        MainGuild = client.GetGuildAsync(options.Value.MainGuildId).GetAwaiter().GetResult();
    }


    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // This tells Discord we are using slash commands
        await Client.InitializeAsync();

        // Connect our bot to the Discord API
        await Client.ConnectAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Client.AllShardsConnected)
        {
            await Client.DisconnectAsync();
        }
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddDiscordClient(builder.Configuration["BOT_TOKEN"]!, DiscordIntents.GuildMembers)
            .Configure<DiscordConfiguration>(builder.Configuration.GetSection("DiscordBotConfig"))
            .AddCommandsExtension((_, extension) =>
            {
                // Configure to slash commands
                extension.AddProcessor(new SlashCommandProcessor());

                // Add our commands from our code (anything with the command
                // decorator)
                extension.AddCommands(Assembly.GetExecutingAssembly());
            })
            .ConfigureEventHandlers(events =>
            {
                Type[] types = Assembly.GetExecutingAssembly().GetTypes();
                List<Type> eventHandlers = types.Where(t => t.IsAssignableTo(typeof(IEventHandler))).ToList();
                events.AddEventHandlers(eventHandlers);
            })
            .AddInjectableHostedService<DiscordService>()
            .ConfigureTypeSection<DiscordServiceOptions>(builder.Configuration);
    }

    public async Task HandleEventAsync(DiscordClient sender, SessionCreatedEventArgs eventArgs)
    {
        // Connecting changes the guild in the cache, so reset it to the one
        // we like
        (Client.Guilds as IDictionary<ulong, DiscordGuild>)![MainGuild.Id] = MainGuild;

        // Make sure CurrentMember is not null
        FieldInfo memberField = typeof(DiscordGuild).GetField("members", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IDictionary<ulong, DiscordMember> members = (memberField.GetValue(MainGuild) as IDictionary<ulong, DiscordMember>)!;
        members[Client.CurrentUser.Id] = await MainGuild.GetMemberAsync(Client.CurrentUser.Id);

        sender.Logger.LogInformation("Set current member in cache");
    }
}

public class DiscordServiceOptions
{
    /// <summary>
    /// The id of the main discord guild of the bot.
    /// </summary>
    public ulong MainGuildId { get; set; } = 0;
}
