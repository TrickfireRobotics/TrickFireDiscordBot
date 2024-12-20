using DSharpPlus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using TrickFireDiscordBot.Services;

namespace TrickFireDiscordBot
{
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
                finally
                {
                    host.Dispose();
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

            return builder.Build();
        }
    }
}
