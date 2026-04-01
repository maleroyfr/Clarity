using Avalonia;
using Clarity.Application;
using Clarity.Comparisons;
using Clarity.Collectors.Graph;
using Clarity.Collectors.PowerShell;
using Clarity.Desktop.Services;
using Clarity.Exports;
using Clarity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;

namespace Clarity.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureSerilog();

        try
        {
            var host = CreateHost(args);
            // Migrate DB on startup (local SQLite dev mode)
            host.Services.MigrateDatabaseAsync().GetAwaiter().GetResult();

            AppServiceLocator.ServiceProvider = host.Services;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Clarity terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IHost CreateHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddLogging(b => b.AddSerilog());
                services.AddApplication();
                services.AddInfrastructure(); // SQLite local dev mode
                services.AddGraphCollectors();
                services.AddPowerShellCollectors();
                services.AddExports();
                services.AddComparisons();
                services.AddDesktopViewModels();
            })
            .Build();

    private static void ConfigureSerilog()
    {
        var logDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Clarity", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logDir, "clarity-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}
