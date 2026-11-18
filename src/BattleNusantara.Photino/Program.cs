using BattleNusantara.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Blazor;

namespace BattleNusantara.Photino;

/// <summary>Battle of Nusantara for macOS and Linux: the shared game shell in a Photino window. Solo play needs no server;
/// LAN play connects to a server like every other client. Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging(logging => logging.AddConsole());
        builder.Services.AddScoped<GameClient>();
        builder.RootComponents.Add<GameShell>("#app");
        var app = builder.Build();
        app.MainWindow
            .SetLogVerbosity(Environment.GetEnvironmentVariable("NUSANTARA_DEVTOOLS") == "1" ? 2 : 0)
            .SetTitle("Battle of Nusantara — Syubadubin Studios")
            .SetSize(1440, 900)
            .SetUseOsDefaultSize(false)
            .Center()
            .SetDevToolsEnabled(Environment.GetEnvironmentVariable("NUSANTARA_DEVTOOLS") == "1")
            .SetContextMenuEnabled(false);
        // Smoke runs (CI on Linux and macOS) close the window after a while and report whether the page talked back:
        // any message proves the WebView loaded Blazor, and an interop reply proves the game's scripts ran.
        var smoke = int.TryParse(Environment.GetEnvironmentVariable("NUSANTARA_SMOKE_SECONDS"), out var seconds) && seconds > 0;
        int messages = 0, replies = 0;
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            if (smoke) { Console.Error.WriteLine(error.ExceptionObject); Environment.Exit(1); }
            app.MainWindow.ShowMessage("Battle of Nusantara", error.ExceptionObject.ToString());
        };
        if (smoke)
        {
            app.MainWindow.RegisterWebMessageReceivedHandler((_, message) =>
            {
                Interlocked.Increment(ref messages);
                if (message.Contains("EndInvokeJS", StringComparison.Ordinal)) Interlocked.Increment(ref replies);
            });
            _ = Task.Delay(TimeSpan.FromSeconds(seconds)).ContinueWith(_ => app.MainWindow.Invoke(() => app.MainWindow.Close()));
        }
        app.Run();
        if (smoke)
        {
            Console.WriteLine($"SMOKE messages={messages} interop-replies={replies}");
            Environment.Exit(messages > 0 && replies > 0 ? 0 : 2);
        }
    }
}
