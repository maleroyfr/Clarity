using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Onboarding;
using Clarity.Collectors.PowerShell;
using Clarity.Application.Environments.Commands;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using MediatR;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed partial class OnboardingWizardViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CurrentStepViewModel))]
    private int _currentStep;

    public ObservableObject CurrentStepViewModel => CurrentStep switch
    {
        0 => WorkloadStep,
        1 => PrerequisitesStep,
        2 => SummaryStep,
        _ => WorkloadStep
    };

    public bool CanGoNext => CurrentStep < 2 && CurrentStepCanProceed();
    public bool CanGoPrev => CurrentStep > 0;
    public bool IsLastStep => CurrentStep == 2;

    public Guid TargetCustomerId { get; set; }

    public WorkloadSelectionStepViewModel WorkloadStep { get; }
    public PrerequisitesStepViewModel PrerequisitesStep { get; }
    public SummaryStepViewModel SummaryStep { get; }

    public event Action? Finished;
    public event Action? Cancelled;

    public OnboardingWizardViewModel(
        IMediator mediator,
        IAzureCliSetupScriptGenerator scriptGenerator,
        IPowerShellPrerequisiteService powerShellPrerequisiteService)
    {
        _mediator = mediator;
        WorkloadStep = new WorkloadSelectionStepViewModel();
        PrerequisitesStep = new PrerequisitesStepViewModel(powerShellPrerequisiteService);
        SummaryStep = new SummaryStepViewModel(scriptGenerator);

        foreach (var workload in WorkloadStep.Workloads)
        {
            workload.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(WorkloadItemVm.IsSelected))
                {
                    OnPropertyChanged(nameof(CanGoNext));
                }
            };
        }
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        if (!CanGoNext) return;

        if (CurrentStep == 0)
        {
            await PrerequisitesStep.BuildFromAsync(WorkloadStep.SelectedAreas);
        }
        else if (CurrentStep == 1)
        {
            SummaryStep.Update(
                WorkloadStep.SelectedAreas,
                PrerequisitesStep.Prerequisites.Count(p => p.IsCompleted),
                PrerequisitesStep.TotalCount);
        }

        CurrentStep++;
    }

    [RelayCommand]
    public void Prev()
    {
        if (CanGoPrev) CurrentStep--;
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        if (string.IsNullOrWhiteSpace(SummaryStep.EnvironmentName))
            return;

        if (TargetCustomerId == Guid.Empty)
        {
            AppServiceLocator.Get<AppShellViewModel>().ShowToast(
                "Customer Required",
                "Please select a customer before completing onboarding. Navigate to Customers first.",
                Avalonia.Controls.Notifications.NotificationType.Warning);
            return;
        }

        Guid? tenantId = Guid.TryParse(SummaryStep.TenantId, out var tid) ? tid : null;

        var cmd = new CreateEnvironmentCommand(
            TargetCustomerId,
            SummaryStep.EnvironmentName.Trim(),
            Clarity.Domain.Environments.EnvironmentType.M365Tenant,
            null,
            tenantId,
            null,
            WorkloadStep.SelectedAreas.Select(a => a.ToString()).ToList());

        await _mediator.Send(cmd);
        Finished?.Invoke();
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke();

    private bool CurrentStepCanProceed() => CurrentStep switch
    {
        0 => WorkloadStep.HasSelection,
        1 => true,
        _ => true
    };
}
