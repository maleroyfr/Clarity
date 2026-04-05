using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.Styling;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string DataFolder = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Clarity");

    private static readonly string DbPath = Path.Combine(DataFolder, "clarity.db");

    private static readonly string LogFolder = Path.Combine(DataFolder, "logs");

    [ObservableProperty]
    private int _themeIndex;

    public string DatabaseInfo { get; } = DbPath;

    public string LogDirectoryInfo { get; } = LogFolder;

    public string VersionInfo { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public string RuntimeInfo { get; } = $".NET {System.Environment.Version}";

    public string OsInfo { get; } = RuntimeInformation.OSDescription;

    partial void OnThemeIndexChanged(int value)
    {
        var faTheme = Avalonia.Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        if (faTheme is not null)
            faTheme.PreferSystemTheme = value == 0;

        if (Avalonia.Application.Current is null) return;
        Avalonia.Application.Current.RequestedThemeVariant = value switch
        {
            1 => Avalonia.Styling.ThemeVariant.Light,
            2 => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        var dir = Path.GetDirectoryName(DbPath);
        if (dir is not null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (Directory.Exists(LogFolder))
            Process.Start(new ProcessStartInfo(LogFolder) { UseShellExecute = true });
    }
}
