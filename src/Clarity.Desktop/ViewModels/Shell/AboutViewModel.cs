using System;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName { get; } = "Clarity";
    public string Tagline { get; } = "Audit & Discovery Platform for Microsoft 365 and Active Directory";
    public string Description { get; } = "A multi-customer, multi-tenant audit platform built for consulting discovery missions. Inventory, normalize, compare, and export data from Entra ID, Intune, Exchange Online, SharePoint Online, and on-premises Active Directory.";
    public string Version { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";
    public string Copyright { get; } = $"\u00a9 {DateTime.Now.Year} Clarity Team. All rights reserved.";
    public string DotNetVersion { get; } = $".NET {Environment.Version}";
    public string AvaloniaVersion { get; } = typeof(Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "11.x";
    public string OsVersion { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
    public string GitHubUrl { get; } = "https://github.com/maleroyfr/Clarity";
    public string License { get; } = "MIT License";
    public string Authors { get; } = "Clarity Team — maleroyfr";
    public string UiFramework { get; } = "Avalonia UI + SukiUI";
    public string Language { get; } = $"{System.Globalization.CultureInfo.CurrentUICulture.DisplayName} ({System.Globalization.CultureInfo.CurrentUICulture.Name})";

    public event Action? CloseRequested;

    [RelayCommand]
    public void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(GitHubUrl)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* ignore if browser can't open */ }
    }
}
