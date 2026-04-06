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
    public string WorkloadHint { get; init; } = string.Empty;
    public bool IsOptional { get; init; }
    public bool IsAutoDetectable { get; init; }
    public bool IsAutoFixSupported { get; init; }

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _statusText = "Manual verification required";

    [ObservableProperty]
    private string? _installScope;

    [ObservableProperty]
    private bool _isInstalling;

    public bool ShowInstallButton => IsAutoFixSupported && !IsCompleted && !IsInstalling;

    public void NotifyInstallButton()
    {
        OnPropertyChanged(nameof(ShowInstallButton));
    }
}

/// <summary>Groups related prerequisite items under a logical heading.</summary>
public sealed partial class PrerequisiteGroupVm : ObservableObject
{
    public string GroupName { get; init; } = default!;
    public string GroupDescription { get; init; } = default!;
    public string GroupIcon { get; init; } = default!;
    public bool IsOptionalGroup { get; init; }
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

/// <summary>Read-only display of required Graph API permissions (not checkboxes).</summary>
public sealed class GraphPermissionDisplayVm
{
    public string PermissionName { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string WorkloadHint { get; init; } = default!;
    public bool IsOptional { get; init; }
}

public sealed partial class PrerequisitesStepViewModel : ObservableObject
{
    private readonly IPowerShellPrerequisiteService _powerShellPrerequisiteService;
    private IReadOnlyList<WorkloadArea> _selectedAreas = [];

    public ObservableCollection<PrerequisiteGroupVm> Groups { get; } = [];
    public ObservableCollection<PrerequisiteItemVm> Prerequisites { get; } = [];
    public ObservableCollection<GraphPermissionDisplayVm> GraphPermissions { get; } = [];

    public bool HasGraphPermissions => GraphPermissions.Count > 0;
    public int GraphPermissionCount => GraphPermissions.Count;
    public string GraphPermissionSummary => $"{GraphPermissions.Count(p => !p.IsOptional)} required, {GraphPermissions.Count(p => p.IsOptional)} optional";

    [ObservableProperty]
    private bool _graphPermissionsExpanded;

    public int TotalCount => Prerequisites.Count;
    public int RequiredCount => Prerequisites.Count(p => !p.IsOptional);
    public int CompletedCount => Prerequisites.Count(p => p.IsCompleted);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGraphPermissions))]
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
        GraphPermissions.Clear();

        // Build Graph permission display (read-only, no checkboxes)
        BuildGraphPermissionDisplay();

        var mergedItems = _selectedAreas
            .SelectMany(WorkloadCapabilityCatalog.BuildChecklistItems)
            .Where(i => i.Category != PrerequisiteCategory.GraphPermission) // excluded from checklist
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

        // Cloud setup prerequisites (app reg, admin consent, certificate — not individual Graph perms)
        if (cloudWorkloads.Count > 0)
        {
            var cloudAuthItems = mergedItems
                .Where(i => i.Category is PrerequisiteCategory.Certificate or PrerequisiteCategory.AdminConsent
                         || i.Key.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase)
                         || i.Key.StartsWith("auth:exchange", StringComparison.OrdinalIgnoreCase)
                         || i.Key.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => !i.IsRequired).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cloudAuthItems.Count > 0)
                AddGroup("App Registration & Consent",
                    "Create an app registration in the customer tenant, grant admin consent, and configure authentication.",
                    "🔐", false, FormatWorkloadHints(cloudWorkloads), cloudAuthItems);
        }

        // PowerShell modules
        var psItems = mergedItems
            .Where(i => i.Category == PrerequisiteCategory.PowerShellModule)
            .OrderBy(i => !i.IsRequired).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (psItems.Count > 0)
        {
            var psWorkloads = _selectedAreas
                .Where(a => WorkloadCapabilityCatalog.GetDefinition(a).RequiredPowerShellModules.Count > 0
                         || mergedItems.Any(i => i.Key == "tool:pwsh"))
                .ToList();
            AddGroup("PowerShell Modules",
                "Auto-detected modules. Install missing ones directly or use the buttons below.",
                "⚡", true, FormatWorkloadHints(psWorkloads), psItems);
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

            var allOnPremItems = new List<WorkloadChecklistItemDefinition>();
            allOnPremItems.AddRange(networkItems);
            allOnPremItems.AddRange(onPremAuthItems);

            if (allOnPremItems.Count > 0)
                AddGroup("On-Premises Access",
                    "Network connectivity and authentication for on-premises workloads.",
                    "🏢", false, FormatWorkloadHints(onPremWorkloads), allOnPremItems);
        }

        // Remaining
        var categorizedKeys = new HashSet<string>(
            Groups.SelectMany(g => g.Items).Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        var remainingItems = mergedItems.Where(i => !categorizedKeys.Contains(i.Key)).ToList();
        if (remainingItems.Count > 0)
            AddGroup("Additional Requirements", "Other prerequisites for selected workloads.",
                "📋", false, "", remainingItems);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RequiredCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(HasGraphPermissions));
        OnPropertyChanged(nameof(GraphPermissionCount));
        OnPropertyChanged(nameof(GraphPermissionSummary));

        await RefreshAsync();
    }

    private void BuildGraphPermissionDisplay()
    {
        // Build a map of permission -> workloads that need it
        var permWorkloads = new Dictionary<string, List<WorkloadArea>>(StringComparer.Ordinal);
        var permDetails = new Dictionary<string, (string Description, bool IsOptional)>(StringComparer.Ordinal);

        foreach (var area in _selectedAreas.Where(AuthTypeHelper.IsCloudWorkload))
        {
            var def = WorkloadCapabilityCatalog.GetDefinition(area);
            foreach (var p in def.RequiredPermissions)
            {
                if (!permWorkloads.ContainsKey(p.Name))
                {
                    permWorkloads[p.Name] = [];
                    permDetails[p.Name] = (p.Description, false);
                }
                permWorkloads[p.Name].Add(area);
            }
            foreach (var p in def.OptionalPermissions)
            {
                if (!permWorkloads.ContainsKey(p.Name))
                {
                    permWorkloads[p.Name] = [];
                    permDetails[p.Name] = (p.Description, true);
                }
                permWorkloads[p.Name].Add(area);
            }
        }

        foreach (var kvp in permWorkloads.OrderBy(k => permDetails[k.Key].IsOptional).ThenBy(k => k.Key))
        {
            var (desc, isOpt) = permDetails[kvp.Key];
            GraphPermissions.Add(new GraphPermissionDisplayVm
            {
                PermissionName = kvp.Key,
                Description = desc,
                WorkloadHint = string.Join(", ", kvp.Value.Select(GetWorkloadShortName).Distinct()),
                IsOptional = isOpt
            });
        }
    }

    private static string FormatWorkloadHints(IEnumerable<WorkloadArea> areas) =>
        string.Join(", ", areas.Select(GetWorkloadShortName));

    private void AddGroup(string name, string description, string icon, bool isOptional,
        string workloadHints, IReadOnlyList<WorkloadChecklistItemDefinition> items)
    {
        // Compute workload hints per item
        var itemWorkloadMap = BuildItemWorkloadMap();

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
            var hint = itemWorkloadMap.TryGetValue(item.Key, out var workloads)
                ? string.Join(", ", workloads.Select(GetWorkloadShortName).Distinct())
                : string.Empty;

            var vm = new PrerequisiteItemVm
            {
                Key = item.Key,
                CategoryKind = item.Category,
                Category = GetCategoryLabel(item.Category),
                Label = item.Label,
                Description = item.Description,
                WorkloadHint = hint,
                IsOptional = !item.IsRequired,
                IsAutoDetectable = item.IsAutoDetectable,
                IsAutoFixSupported = item.IsAutoFixSupported,
                StatusText = item.IsAutoDetectable ? "Checking…" : "Manual verification required"
            };

            group.Items.Add(vm);
            Prerequisites.Add(vm);
        }

        Groups.Add(group);
    }

    private Dictionary<string, List<WorkloadArea>> BuildItemWorkloadMap()
    {
        var map = new Dictionary<string, List<WorkloadArea>>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in _selectedAreas)
        {
            foreach (var item in WorkloadCapabilityCatalog.BuildChecklistItems(area))
            {
                if (!map.ContainsKey(item.Key))
                    map[item.Key] = [];
                map[item.Key].Add(area);
            }
        }
        return map;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_selectedAreas.Count == 0) return;

        IsChecking = true;
        StatusMessage = "Checking local prerequisites…";

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

            StatusMessage = "Verification complete. Check items above and confirm manually where needed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Verification failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
            RefreshAllGroupCounts();
            OnPropertyChanged(nameof(CompletedCount));
        }
    }

    [RelayCommand]
    public async Task InstallModuleAsync(PrerequisiteItemVm item)
    {
        if (item is not { IsAutoFixSupported: true, IsCompleted: false }) return;

        item.IsInstalling = true;
        item.NotifyInstallButton();
        item.StatusText = "Installing…";

        try
        {
            // Extract module name from key "module:ModuleName"
            var moduleName = item.Key.StartsWith("module:", StringComparison.OrdinalIgnoreCase)
                ? item.Key[7..] : item.Key;
            var requirement = WorkloadCapabilityCatalog.GetRequiredModules(_selectedAreas)
                .FirstOrDefault(m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

            if (requirement is null)
            {
                item.StatusText = "Unknown module";
                return;
            }

            var result = await _powerShellPrerequisiteService.InstallModuleAsync(
                requirement.ModuleName, requirement.MinimumVersion);

            if (result.Success)
            {
                item.IsCompleted = true;
                item.StatusText = $"Installed ({result.InstalledVersion})";
            }
            else
            {
                item.StatusText = $"Install failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            item.IsInstalling = false;
            item.NotifyInstallButton();
            RefreshAllGroupCounts();
            OnPropertyChanged(nameof(CompletedCount));
        }
    }

    private void ResetAutoDetectableItems()
    {
        foreach (var item in Prerequisites.Where(item => item.IsAutoDetectable))
        {
            item.IsCompleted = false;
            item.StatusText = "Checking…";
            item.InstallScope = null;
            item.NotifyInstallButton();
        }
    }

    private void ApplyPwshStatus(PwshStatus status)
    {
        var pwshItem = Prerequisites.FirstOrDefault(item =>
            item.Key.Equals("tool:pwsh", StringComparison.OrdinalIgnoreCase));

        if (pwshItem is null) return;

        pwshItem.IsCompleted = status.Available;
        pwshItem.StatusText = status.Available
            ? $"✓ PowerShell {status.Version} detected"
            : "✗ PowerShell 7 not found on PATH";
        pwshItem.NotifyInstallButton();
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
                { Installed: true, Scope: not null } => $"✓ {module.InstalledVersion} — {module.Scope} scope",
                { Installed: true } => $"✓ {module.InstalledVersion}",
                { NeedsUpgrade: true } => $"⚠ Version {module.InstalledVersion} < {module.MinimumVersion}",
                { Error: not null } => $"✗ {module.Error}",
                _ => "✗ Not installed"
            };
            item.NotifyInstallButton();
        }
    }

    private void RefreshAllGroupCounts()
    {
        foreach (var group in Groups)
            group.RefreshCounts();
    }

    private static string GetWorkloadShortName(WorkloadArea area) => area switch
    {
        WorkloadArea.EntraId => "Entra ID",
        WorkloadArea.Intune => "Intune",
        WorkloadArea.ExchangeOnline => "Exchange Online",
        WorkloadArea.SharePointOnline => "SharePoint Online",
        WorkloadArea.Teams => "Teams",
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
