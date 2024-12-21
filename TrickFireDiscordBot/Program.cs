using DSharpPlus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Reflection;
using TrickFireDiscordBot.Services;

namespace TrickFireDiscordBot;

internal class Program
{
    private static async Task Main(string[] args)
    {
        string baseDir = args.Length > 0 ? args[0] : "";
        string[] secrets = File.ReadAllLines(Path.Join(baseDir, "secrets.txt"));

        IHost host;
        try
        {
            host = CreateHost(baseDir, secrets);
        }
        catch (Exception e)
        {
            Console.WriteLine("Startup error. The bot is likely not in a working state: " + e.ToString());
            await Task.Delay(-1);
            return;
        }
        
        ILogger logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            await host.StartAsync();

            // Hang the process forever so it doesn't quit after the bot
            // connects
            await host.WaitForShutdownAsync();
        }
        finally
        {
            // Dispose of discord client before host, since it needs the
            // service provider
            try
            {
                host.Services.GetService<DiscordClient>()?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispose of discord client on shutdown");
            }
            finally
            {
                try
                {
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to dispose of host on shutdown");
                }
            }
        }
    }

    private static IHost CreateHost(string baseDir, string[] secrets)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Add config.json
        builder.Configuration.AddJsonFile(Path.Join(baseDir, "config.json"));
        builder.Configuration["BOT_TOKEN"] = secrets[0];
        builder.Configuration["NOTION_SECRET"] = secrets[1];

        builder.Services.ConfigureTypeSection<HostOptions>(builder.Configuration);

        // Get all types
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (Type type in asm.GetTypes())
        {
            // Filter to those that implement auto registered service
            if (!typeof(IAutoRegisteredService).IsAssignableFrom(type) || type == typeof(IAutoRegisteredService))
            {
                continue;
            }

            // Register type
            MethodInfo registerMethod = type.GetMethod(nameof(IAutoRegisteredService.Register), [typeof(IHostApplicationBuilder)])!;
            registerMethod.Invoke(null, [builder]);
        }

        // Setup logging
        builder.Logging
            .AddConsole(opt =>
            {
                opt.FormatterName = "logger";
            })
            .AddConsoleFormatter<LoggerFormatter, ConsoleFormatterOptions>(opt =>
            {
                opt.TimestampFormat = "yy-MM-dd HH:mm:ss";
                opt.UseUtcTimestamp = false;
                opt.IncludeScopes = true;
            });

        return builder.Build();
    }
}
