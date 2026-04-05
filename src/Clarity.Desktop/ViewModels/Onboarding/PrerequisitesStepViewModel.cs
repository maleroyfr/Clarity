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

    [ObservableProperty]
    private string? _installScope;
}

/// <summary>Groups related prerequisite items under a logical heading.</summary>
public sealed partial class PrerequisiteGroupVm : ObservableObject
{
    public string GroupName { get; init; } = default!;
    public string GroupDescription { get; init; } = default!;
    public string GroupIcon { get; init; } = default!;
    public bool IsOptionalGroup { get; init; }

    /// <summary>Workload names that require this group (displayed as hint chips).</summary>
    public string WorkloadHints { get; init; } = string.Empty;

    public ObservableCollection<PrerequisiteItemVm> Items { get; init; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    public int TotalCount => Items.Count;
    public int CompletedCount => Items.Count(i => i.IsCompleted);
    public string CountText => $"{CompletedCount} / {TotalCount}";

    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(CountText));
    }
}

public sealed partial class PrerequisitesStepViewModel : ObservableObject
{
    private readonly IPowerShellPrerequisiteService _powerShellPrerequisiteService;
    private IReadOnlyList<WorkloadArea> _selectedAreas = [];

    public ObservableCollection<PrerequisiteGroupVm> Groups { get; } = [];
    public ObservableCollection<PrerequisiteItemVm> Prerequisites { get; } = [];

    public int TotalCount => Prerequisites.Count;
    public int RequiredCount => Prerequisites.Count(p => !p.IsOptional);
    public int CompletedCount => Prerequisites.Count(p => p.IsCompleted);
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
        _selectedAreas = selectedAreas.Distinct().ToList();

        Prerequisites.Clear();
        Groups.Clear();

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
            .ToList();

        var cloudWorkloads = _selectedAreas.Where(AuthTypeHelper.IsCloudWorkload).ToList();
        var onPremWorkloads = _selectedAreas.Where(AuthTypeHelper.IsOnPremWorkload).ToList();

        // Group by area: Microsoft 365 Cloud prerequisites
        if (cloudWorkloads.Count > 0)
        {
            var cloudGraphItems = mergedItems
                .Where(i => i.Category == PrerequisiteCategory.GraphPermission)
                .OrderBy(i => !i.IsRequired).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cloudAuthItems = mergedItems
                .Where(i => i.Category is PrerequisiteCategory.Certificate or PrerequisiteCategory.AdminConsent
                         || i.Key.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase)
                         || i.Key.StartsWith("auth:exchange", StringComparison.OrdinalIgnoreCase)
                         || i.Key.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
                .Where(i => i.Category != PrerequisiteCategory.GraphPermission)
                .OrderBy(i => !i.IsRequired).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cloudPsItems = mergedItems
                .Where(i => i.Category == PrerequisiteCategory.PowerShellModule
                    && !i.Key.StartsWith("auth:ad", StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => !i.IsRequired).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allCloudItems = new List<WorkloadChecklistItemDefinition>();
            allCloudItems.AddRange(cloudAuthItems);
            allCloudItems.AddRange(cloudGraphItems);
            allCloudItems.AddRange(cloudPsItems);

            if (allCloudItems.Count > 0)
                AddGroup("Microsoft 365 Cloud",
                    "App registration, Graph API permissions, and PowerShell modules for cloud workloads.",
                    "☁️", false, FormatWorkloadHints(cloudWorkloads), allCloudItems);
        }

        // On-Premises prerequisites
        if (onPremWorkloads.Count > 0)
        {
            var networkItems = mergedItems
                .Where(i => i.Category == PrerequisiteCategory.NetworkAccess)
                .ToList();

            var onPremAuthItems = mergedItems
                .Where(i => i.Key.StartsWith("auth:ad", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var onPremPsRuntime = mergedItems
                .Where(i => i.Key.Equals("tool:pwsh", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var allOnPremItems = new List<WorkloadChecklistItemDefinition>();
            allOnPremItems.AddRange(onPremPsRuntime);
            allOnPremItems.AddRange(networkItems);
            allOnPremItems.AddRange(onPremAuthItems);

            if (allOnPremItems.Count > 0)
                AddGroup("On-Premises Infrastructure",
                    "Network connectivity, LDAP access, and authentication for on-premises workloads.",
                    "🏢", false, FormatWorkloadHints(onPremWorkloads), allOnPremItems);
        }

        // Remaining items not covered
        var categorizedKeys = new HashSet<string>(
            Groups.SelectMany(g => g.Items).Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        var remainingItems = mergedItems.Where(i => !categorizedKeys.Contains(i.Key)).ToList();
        if (remainingItems.Count > 0)
            AddGroup("Additional Requirements", "Other prerequisites for selected workloads.",
                "📋", false, "", remainingItems);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RequiredCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(CanInstallMissingModules));

        await RefreshAsync();
    }

    private static string FormatWorkloadHints(IEnumerable<WorkloadArea> areas) =>
        string.Join(", ", areas.Select(GetWorkloadShortName));

    private void AddGroup(string name, string description, string icon, bool isOptional,
        string workloadHints, IReadOnlyList<WorkloadChecklistItemDefinition> items)
    {
        var group = new PrerequisiteGroupVm
        {
            GroupName = name,
            GroupDescription = description,
            GroupIcon = icon,
            IsOptionalGroup = isOptional,
            WorkloadHints = workloadHints
        };

        foreach (var item in items)
        {
            var vm = new PrerequisiteItemVm
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
            };

            group.Items.Add(vm);
            Prerequisites.Add(vm);
        }

        Groups.Add(group);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_selectedAreas.Count == 0) return;

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
            RefreshAllGroupCounts();
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(CanInstallMissingModules));
        }
    }

    [RelayCommand]
    public async Task InstallMissingModulesAsync()
    {
        if (!CanInstallMissingModules) return;

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
            RefreshAllGroupCounts();
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(CanInstallMissingModules));
        }
    }

    private void ResetAutoDetectableItems()
    {
        foreach (var item in Prerequisites.Where(item => item.IsAutoDetectable))
        {
            item.IsCompleted = false;
            item.StatusText = "Waiting for verification";
            item.InstallScope = null;
        }
    }

    private void ApplyPwshStatus(PwshStatus status)
    {
        var pwshItem = Prerequisites.FirstOrDefault(item =>
            item.Key.Equals("tool:pwsh", StringComparison.OrdinalIgnoreCase));

        if (pwshItem is null) return;

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

            if (item is null) continue;

            item.IsCompleted = module.Installed;
            item.InstallScope = module.Scope;
            item.StatusText = module switch
            {
                { Installed: true, Scope: not null } => $"Installed ({module.InstalledVersion}) — {module.Scope} scope",
                { Installed: true } => $"Installed ({module.InstalledVersion})",
                { NeedsUpgrade: true } => $"Installed version {module.InstalledVersion} is below {module.MinimumVersion}",
                { Error: not null } => module.Error,
                _ => "Module not installed"
            };
        }
    }

    private void RefreshAllGroupCounts()
    {
        foreach (var group in Groups)
            group.RefreshCounts();
    }

    private static string GetWorkloadShortName(WorkloadArea area) => area switch
    {
        WorkloadArea.EntraId => "Microsoft Entra ID",
        WorkloadArea.Intune => "Microsoft Intune",
        WorkloadArea.ExchangeOnline => "Exchange Online",
        WorkloadArea.SharePointOnline => "SharePoint Online",
        WorkloadArea.Teams => "Microsoft Teams",
        WorkloadArea.OnPremAD => "Active Directory",
        WorkloadArea.OnPremExchange => "Exchange Server",
        _ => area.ToString()
    };

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
