using CommunityToolkit.Mvvm.ComponentModel;
using Clarity.SharedContracts.Enums;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed partial class SummaryStepViewModel : ObservableObject
{
    [ObservableProperty]
    private string _environmentName = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private int _selectedWorkloadCount;

    [ObservableProperty]
    private int _prerequisitesMetCount;

    [ObservableProperty]
    private int _prerequisitesTotalCount;

    public IReadOnlyList<WorkloadArea> SelectedWorkloads { get; private set; } = [];

    public void Update(
        IReadOnlyList<WorkloadArea> selectedWorkloads,
        int prerequisitesMet,
        int prerequisitesTotal)
    {
        SelectedWorkloads = selectedWorkloads;
        SelectedWorkloadCount = selectedWorkloads.Count;
        PrerequisitesMetCount = prerequisitesMet;
        PrerequisitesTotalCount = prerequisitesTotal;
    }
}
