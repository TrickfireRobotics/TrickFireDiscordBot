using TrickfireCheckIn.Discord;

namespace TrickfireCheckIn
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // The token is on the first line of the secrets.txt file
            string token;
            if (args.Length == 1)
            {
                token = File.ReadAllLines(args[0])[0];
            }
            else
            {
                token = File.ReadAllLines("secrets.txt")[0];
            }

            // Start the bot
            Bot bot = new(token);
            await bot.Start();

            // Hang the process forever so it doesn't quit after the bot
            // connects
            await Task.Delay(-1);
        }
    }
}
