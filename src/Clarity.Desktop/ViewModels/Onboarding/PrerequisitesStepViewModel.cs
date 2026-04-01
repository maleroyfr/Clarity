using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Collectors.PowerShell;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed partial class PrerequisiteItemVm : ObservableObject
{
    public string Key { get; init; } = default!;
    public PrerequisiteCategory CategoryKind { get; init; }
    public string Category { get; init; } = default!;
    public string Label { get; init; } = default!;
    public string Description { get; init; } = default!;
    public bool IsOptional { get; init; }
    public bool IsAutoDetectable { get; init; }
    public bool IsAutoFixSupported { get; init; }

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _statusText = "Manual verification required";
}

public sealed partial class PrerequisitesStepViewModel : ObservableObject
{
    private readonly IPowerShellPrerequisiteService _powerShellPrerequisiteService;
    private IReadOnlyList<WorkloadArea> _selectedAreas = [];

    public ObservableCollection<PrerequisiteItemVm> Prerequisites { get; } = [];

    public int TotalCount => Prerequisites.Count;
    public int RequiredCount => Prerequisites.Count(p => !p.IsOptional);
    public bool CanInstallMissingModules =>
        !IsChecking &&
        Prerequisites.Any(item =>
            item.CategoryKind == PrerequisiteCategory.PowerShellModule &&
            item.IsAutoFixSupported &&
            !item.IsCompleted);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallMissingModules))]
    private bool _isChecking;

    [ObservableProperty]
    private string? _statusMessage;

    public PrerequisitesStepViewModel(IPowerShellPrerequisiteService powerShellPrerequisiteService)
    {
        _powerShellPrerequisiteService = powerShellPrerequisiteService;
    }

    public async Task BuildFromAsync(IReadOnlyList<WorkloadArea> selectedAreas)
    {
        _selectedAreas = selectedAreas
            .Distinct()
            .ToList();

        Prerequisites.Clear();

        var mergedItems = _selectedAreas
            .SelectMany(WorkloadCapabilityCatalog.BuildChecklistItems)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new WorkloadChecklistItemDefinition(
                    first.Key,
                    first.Label,
                    string.Join(" ", group.Select(item => item.Description).Distinct(StringComparer.Ordinal)),
                    first.Category,
                    IsRequired: group.Any(item => item.IsRequired),
                    IsAutoDetectable: group.Any(item => item.IsAutoDetectable),
                    IsAutoFixSupported: group.Any(item => item.IsAutoFixSupported));
            })
            .OrderBy(item => item.IsRequired ? 0 : 1)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase);

        foreach (var item in mergedItems)
        {
            Prerequisites.Add(new PrerequisiteItemVm
            {
                Key = item.Key,
                CategoryKind = item.Category,
                Category = GetCategoryLabel(item.Category),
                Label = item.Label,
                Description = item.Description,
                IsOptional = !item.IsRequired,
                IsAutoDetectable = item.IsAutoDetectable,
                IsAutoFixSupported = item.IsAutoFixSupported,
                StatusText = item.IsAutoDetectable ? "Waiting for verification" : "Manual verification required"
            });
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RequiredCount));
        OnPropertyChanged(nameof(CanInstallMissingModules));

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_selectedAreas.Count == 0)
        {
            return;
        }

        IsChecking = true;
        StatusMessage = "Checking local prerequisite readiness...";

        try
        {
            ResetAutoDetectableItems();

            var hasPowerShellPrereqs = Prerequisites.Any(item =>
                item.Key.Equals("tool:pwsh", StringComparison.OrdinalIgnoreCase) ||
                item.CategoryKind == PrerequisiteCategory.PowerShellModule);

            PwshStatus? pwshStatus = null;
            if (hasPowerShellPrereqs)
            {
                pwshStatus = await _powerShellPrerequisiteService.CheckPwshAsync();
                ApplyPwshStatus(pwshStatus);
            }

            if (pwshStatus is null || pwshStatus.Available)
            {
                var modules = await _powerShellPrerequisiteService.CheckModulesAsync(_selectedAreas);
                ApplyModuleStatuses(modules);
            }

            StatusMessage = "Prerequisite verification complete. Manual items still require consultant confirmation.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Prerequisite verification failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(CanInstallMissingModules));
        }
    }

    [RelayCommand]
    public async Task InstallMissingModulesAsync()
    {
        if (!CanInstallMissingModules)
        {
            return;
        }

        IsChecking = true;
        StatusMessage = "Installing missing PowerShell modules...";

        try
        {
            var results = await _powerShellPrerequisiteService.InstallAllMissingAsync(_selectedAreas);
            var failures = results.Count(result => !result.Success);

            StatusMessage = failures == 0
                ? "PowerShell module installation completed successfully."
                : $"{failures} module installation step(s) failed. Review the checklist status.";

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Module installation failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(CanInstallMissingModules));
        }
    }

    private void ResetAutoDetectableItems()
    {
        foreach (var item in Prerequisites.Where(item => item.IsAutoDetectable))
        {
            item.IsCompleted = false;
            item.StatusText = "Waiting for verification";
        }
    }

    private void ApplyPwshStatus(PwshStatus status)
    {
        var pwshItem = Prerequisites.FirstOrDefault(item =>
            item.Key.Equals("tool:pwsh", StringComparison.OrdinalIgnoreCase));

        if (pwshItem is null)
        {
            return;
        }

        pwshItem.IsCompleted = status.Available;
        pwshItem.StatusText = status.Available
            ? $"Detected PowerShell {status.Version}"
            : "PowerShell 7 was not detected on PATH";
    }

    private void ApplyModuleStatuses(IReadOnlyList<ModuleStatus> modules)
    {
        foreach (var module in modules)
        {
            var item = Prerequisites.FirstOrDefault(prerequisite =>
                prerequisite.Key.Equals($"module:{module.ModuleName}", StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                continue;
            }

            item.IsCompleted = module.Installed;
            item.StatusText = module switch
            {
                { Installed: true } => $"Installed ({module.InstalledVersion})",
                { NeedsUpgrade: true } => $"Installed version {module.InstalledVersion} is below {module.MinimumVersion}",
                { Error: not null } => module.Error,
                _ => "Module not installed"
            };
        }
    }

    private static string GetCategoryLabel(PrerequisiteCategory category) =>
        category switch
        {
            PrerequisiteCategory.GraphPermission => "Graph permission",
            PrerequisiteCategory.Certificate => "Authentication",
            PrerequisiteCategory.PowerShellModule => "PowerShell",
            PrerequisiteCategory.NetworkAccess => "Network",
            PrerequisiteCategory.AdminConsent => "Admin consent",
            _ => "Setup"
        };
}
