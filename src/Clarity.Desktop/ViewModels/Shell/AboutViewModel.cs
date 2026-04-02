using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName { get; } = "Clarity";
    public string Description { get; } = "Audit & Discovery Platform\nfor Microsoft 365 and Active Directory";
    public string Version { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";
    public string Copyright { get; } = $"© {DateTime.Now.Year} Clarity. All rights reserved.";
    public string Framework { get; } = $".NET {Environment.Version}";

    public event Action? CloseRequested;

    [RelayCommand]
    public void Close() => CloseRequested?.Invoke();
}
