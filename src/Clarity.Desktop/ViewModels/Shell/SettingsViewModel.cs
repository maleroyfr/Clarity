using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI;
using SukiUI.Enums;
using Avalonia.Styling;

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

    [ObservableProperty]
    private int _colorIndex;

    public string DatabaseInfo { get; } = DbPath;
    public string LogDirectoryInfo { get; } = LogFolder;

    public string VersionInfo { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public string RuntimeInfo { get; } = $".NET {System.Environment.Version}";
    public string OsInfo { get; } = RuntimeInformation.OSDescription;

    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];
    public string[] ColorOptions { get; } = ["Blue", "Green", "Orange", "Red"];

    partial void OnThemeIndexChanged(int value)
    {
        var theme = SukiTheme.GetInstance();
        switch (value)
        {
            case 1:
                theme.ChangeBaseTheme(ThemeVariant.Light);
                break;
            case 2:
                theme.ChangeBaseTheme(ThemeVariant.Dark);
                break;
            default:
                theme.ChangeBaseTheme(ThemeVariant.Default);
                break;
        }
    }

    partial void OnColorIndexChanged(int value)
    {
        var theme = SukiTheme.GetInstance();
        var color = value switch
        {
            1 => SukiColor.Green,
            2 => SukiColor.Orange,
            3 => SukiColor.Red,
            _ => SukiColor.Blue
        };
        theme.ChangeColorTheme(color);
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
