using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace TrickFireDiscordBot
{
    public class WebhookListener : IDisposable
    {
        public event Action<HttpListenerRequest>? OnWebhookReceived;
        public ILogger Logger { get; }

        private readonly HttpListener _listener = new();
        private bool _isRunning = false;

        public WebhookListener(ILogger<WebhookListener> logger, params string[] prefixes)
        {
            Logger = logger;
            foreach (string prefix in prefixes)
            {
                _listener.Prefixes.Add(prefix);
            }
        }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await LongThread();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Webhook Listener error");
                }
            });
        }

        public void Stop()
        {
            _listener.Stop();
        }

        void IDisposable.Dispose()
        {
            GC.SuppressFinalize(this);
            _listener.Close();
        }

        public void Close()
        {
            ((IDisposable)this).Dispose();
        }

        private async Task LongThread()
        {
            _isRunning = true;
            _listener.Start();

            while (_isRunning)
            {
                try
                {
                    // Wait for request
                    HttpListenerContext ctx = await _listener.GetContextAsync();

                    // Read request body
                    OnWebhookReceived?.Invoke(ctx.Request);

                    // Return ok response on request
                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                    ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes("Ok"));
                    ctx.Response.OutputStream.Close();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Webhook Listener loop error");
                }
            }
        }
    }
}
