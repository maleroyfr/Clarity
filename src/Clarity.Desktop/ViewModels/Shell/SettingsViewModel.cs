using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedTheme = "System";

    public string[] AvailableThemes { get; } = ["System", "Light", "Dark"];

    public string AppVersion { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public string DatabasePath { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Clarity", "clarity.db");

    public string LogDirectory { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Clarity", "logs");

    public string Framework { get; } = $".NET {System.Environment.Version}";

    partial void OnSelectedThemeChanged(string value)
    {
        if (Avalonia.Application.Current is null) return;
        Avalonia.Application.Current.RequestedThemeVariant = value switch
        {
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    [RelayCommand]
    public void OpenLogDirectory()
    {
        if (Directory.Exists(LogDirectory))
            Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenDatabaseDirectory()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (dir is not null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
