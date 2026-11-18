using System.Windows;
using BattleNusantara.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BattleNusantara.Desktop;
public partial class MainWindow : Window
{
    private readonly ServiceProvider services;
    public MainWindow()
    {
        var collection = new ServiceCollection();
        collection.AddWpfBlazorWebView();
        collection.AddLogging(logging => logging.AddConsole());
        collection.AddScoped<GameClient>();
        services = collection.BuildServiceProvider();
        InitializeComponent();
        GameView.Services = services;
        Closed += async (_, _) => { await GameView.DisposeAsync(); await services.DisposeAsync(); };
    }
}
