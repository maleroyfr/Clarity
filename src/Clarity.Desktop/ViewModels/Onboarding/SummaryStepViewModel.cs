using CommunityToolkit.Mvvm.ComponentModel;
using Clarity.Application.Onboarding;
using Clarity.SharedContracts.Enums;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed partial class SummaryStepViewModel : ObservableObject
{
    private readonly IAzureCliSetupScriptGenerator _scriptGenerator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetupScript))]
    private string _environmentName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetupScript))]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private int _selectedWorkloadCount;

    [ObservableProperty]
    private int _prerequisitesMetCount;

    [ObservableProperty]
    private int _prerequisitesTotalCount;

    [ObservableProperty]
    private string _requiredPermissionsText = string.Empty;

    [ObservableProperty]
    private string _optionalPermissionsText = string.Empty;

    [ObservableProperty]
    private string _setupScript = string.Empty;

    public IReadOnlyList<WorkloadArea> SelectedWorkloads { get; private set; } = [];
    public bool HasSetupScript => !string.IsNullOrWhiteSpace(SetupScript);

    public SummaryStepViewModel(IAzureCliSetupScriptGenerator scriptGenerator)
    {
        _scriptGenerator = scriptGenerator;
    }

    public void Update(
        IReadOnlyList<WorkloadArea> selectedWorkloads,
        int prerequisitesMet,
        int prerequisitesTotal)
    {
        SelectedWorkloads = selectedWorkloads;
        SelectedWorkloadCount = selectedWorkloads.Count;
        PrerequisitesMetCount = prerequisitesMet;
        PrerequisitesTotalCount = prerequisitesTotal;
        RegenerateSetupArtifacts();
    }

    partial void OnEnvironmentNameChanged(string value) => RegenerateSetupArtifacts();
    partial void OnTenantIdChanged(string value) => RegenerateSetupArtifacts();

    private void RegenerateSetupArtifacts()
    {
        var cloudWorkloads = SelectedWorkloads
            .Where(workload => workload is not WorkloadArea.OnPremAD and not WorkloadArea.OnPremExchange)
            .ToList();

        if (cloudWorkloads.Count == 0)
        {
            RequiredPermissionsText = string.Empty;
            OptionalPermissionsText = string.Empty;
            SetupScript = string.Empty;
            return;
        }

        RequiredPermissionsText = string.Join(
            Environment.NewLine,
            _scriptGenerator.GetRequiredPermissions(cloudWorkloads).Select(permission => $"• {permission}"));

        OptionalPermissionsText = string.Join(
            Environment.NewLine,
            _scriptGenerator.GetOptionalPermissions(cloudWorkloads).Select(permission => $"• {permission}"));

        var appName = string.IsNullOrWhiteSpace(EnvironmentName)
            ? "Clarity Inventory"
            : $"Clarity - {EnvironmentName.Trim()}";

        var resolvedTenantId = string.IsNullOrWhiteSpace(TenantId)
            ? "<tenant-id>"
            : TenantId.Trim();

        SetupScript = _scriptGenerator.GenerateScript(
            resolvedTenantId,
            appName,
            secretLifetimeYears: 2,
            cloudWorkloads);
    }
}
