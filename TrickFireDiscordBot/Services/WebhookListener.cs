using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace TrickFireDiscordBot.Services
{
    public class WebhookListener : BackgroundService, IAutoRegisteredService
    {
        public event Action<HttpListenerRequest>? OnWebhookReceived;

        public ILogger Logger { get; }

        private readonly HttpListener _listener = new();

        public WebhookListener(ILogger<WebhookListener> logger, IOptions<WebhookListenerOptions> options)
        {
            Logger = logger;
            foreach (string prefix in options.Value.Prefixes)
            {
                _listener.Prefixes.Add(prefix);
            }
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _listener.Start();

            return base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);

            _listener.Stop();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for request
                HttpListenerContext ctx;
                ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken);

                // Read request body
                OnWebhookReceived?.Invoke(ctx.Request);

                // Return ok response on request
                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes("Ok"));
                ctx.Response.OutputStream.Close();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            GC.SuppressFinalize(this);
            _listener.Close();
        }

        public static void Register(IHostApplicationBuilder builder)
        {
            builder.Services
                .AddInjectableHostedService<WebhookListener>()
                .ConfigureTypeSection<WebhookListenerOptions>(builder.Configuration);
        }
    }

    public class WebhookListenerOptions()
    {
        public string[] Prefixes { get; set; } = ["http://*:8080/"];
    }
}
