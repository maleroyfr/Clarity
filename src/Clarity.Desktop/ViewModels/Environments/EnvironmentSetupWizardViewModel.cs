using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Commands;
using Clarity.Application.Onboarding;
using Clarity.Collectors.PowerShell;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Onboarding;
using Clarity.Desktop.ViewModels.Shell;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using MediatR;
using Avalonia.Controls.Notifications;

namespace Clarity.Desktop.ViewModels.Environments;

/// <summary>
/// Unified multi-step wizard that replaces both the old EnvironmentFormView (basics only)
/// and the old OnboardingWizardView (workloads only, hardcoded type).
/// Steps: Basics → Workloads → Prerequisites → Summary.
/// Used for both creating new environments and editing existing ones.
/// </summary>
public sealed partial class EnvironmentSetupWizardViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    private Guid _customerId;
    private Guid? _editingId;

    // ── Step tracking ────────────────────────────────────────────────

    public static readonly string[] StepTitles = ["Basics", "Workloads", "Prerequisites", "Summary"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(CurrentStepViewModel))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _currentStep;

    public bool CanGoNext => CurrentStep < 3 && CurrentStepCanProceed();
    public bool CanGoPrev => CurrentStep > 0;
    public bool IsLastStep => CurrentStep == 3;
    public bool IsFirstStep => CurrentStep == 0;
    public string NextButtonText => CurrentStep == 2 ? "Next →" : "Next →";

    public ObservableObject CurrentStepViewModel => CurrentStep switch
    {
        0 => BasicsStep,
        1 => WorkloadStep,
        2 => PrerequisitesStep,
        3 => SummaryStep,
        _ => BasicsStep
    };

    // ── Step ViewModels ──────────────────────────────────────────────

    public EnvironmentBasicsStepViewModel BasicsStep { get; }
    public WorkloadSelectionStepViewModel WorkloadStep { get; }
    public PrerequisitesStepViewModel PrerequisitesStep { get; }
    public SummaryStepViewModel SummaryStep { get; }

    // ── State ────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    public string WizardTitle => IsEditMode ? "Edit Environment" : "New Environment";

    // ── Events ───────────────────────────────────────────────────────

    public event Action? Completed;
    public event Action? Cancelled;

    public EnvironmentSetupWizardViewModel(
        IMediator mediator,
        IAzureCliSetupScriptGenerator scriptGenerator,
        IPowerShellPrerequisiteService powerShellPrerequisiteService)
    {
        _mediator = mediator;
        BasicsStep = new EnvironmentBasicsStepViewModel();
        WorkloadStep = new WorkloadSelectionStepViewModel();
        PrerequisitesStep = new PrerequisitesStepViewModel(powerShellPrerequisiteService);
        SummaryStep = new SummaryStepViewModel(scriptGenerator);

        // Wire up workload selection changes to refresh CanGoNext
        foreach (var workload in WorkloadStep.Workloads)
        {
            workload.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(WorkloadItemVm.IsSelected))
                    OnPropertyChanged(nameof(CanGoNext));
            };
        }

        // Wire up BasicsStep name changes to refresh CanGoNext
        BasicsStep.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EnvironmentBasicsStepViewModel.Name))
                OnPropertyChanged(nameof(CanGoNext));
        };
    }

    /// <summary>Initializes for a new environment or editing an existing one.</summary>
    public void Initialize(EnvironmentDto? existing, Guid customerId)
    {
        _customerId = customerId;
        _editingId = existing?.Id;
        IsEditMode = existing is not null;
        CurrentStep = 0;
        ErrorMessage = null;

        if (existing is not null)
        {
            BasicsStep.Name = existing.Name;
            BasicsStep.Description = existing.Description ?? string.Empty;
            BasicsStep.SelectedTypeOption = EnvironmentTypeOption.All
                .FirstOrDefault(o => o.Value == existing.Type) ?? EnvironmentTypeOption.All[0];
            BasicsStep.TenantId = existing.TenantId?.ToString() ?? string.Empty;
            BasicsStep.TenantDomain = existing.TenantDomain ?? string.Empty;

            // Pre-select workloads that are already enabled
            foreach (var wl in WorkloadStep.Workloads)
            {
                wl.IsSelected = existing.WorkloadAreas.Contains(
                    wl.WorkloadArea.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            BasicsStep.Name = string.Empty;
            BasicsStep.Description = string.Empty;
            BasicsStep.SelectedTypeOption = EnvironmentTypeOption.All[0];
            BasicsStep.TenantId = string.Empty;
            BasicsStep.TenantDomain = string.Empty;

            foreach (var wl in WorkloadStep.Workloads)
                wl.IsSelected = false;
        }

        OnPropertyChanged(nameof(WizardTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        if (!CanGoNext) return;

        if (CurrentStep == 1)
        {
            // Moving from Workloads → Prerequisites: build prerequisite list
            await PrerequisitesStep.BuildFromAsync(WorkloadStep.SelectedAreas);
        }
        else if (CurrentStep == 2)
        {
            // Moving from Prerequisites → Summary: update summary
            SummaryStep.EnvironmentName = BasicsStep.Name;
            SummaryStep.TenantId = BasicsStep.TenantId;
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
        if (string.IsNullOrWhiteSpace(BasicsStep.Name))
        {
            ErrorMessage = "Environment name is required.";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            Guid? parsedTenantId = Guid.TryParse(BasicsStep.TenantId, out var tid) ? tid : null;
            var domain = string.IsNullOrWhiteSpace(BasicsStep.TenantDomain) ? null : BasicsStep.TenantDomain.Trim();
            var workloadAreas = WorkloadStep.SelectedAreas.Select(a => a.ToString()).ToList();

            if (IsEditMode && _editingId.HasValue)
            {
                // Update basic fields
                await _mediator.Send(new UpdateEnvironmentCommand(
                    _editingId.Value, BasicsStep.Name.Trim(),
                    string.IsNullOrWhiteSpace(BasicsStep.Description) ? null : BasicsStep.Description.Trim(),
                    domain));

                // Update workloads
                await _mediator.Send(new SetEnvironmentWorkloadsCommand(
                    _editingId.Value,
                    WorkloadStep.SelectedAreas));
            }
            else
            {
                await _mediator.Send(new CreateEnvironmentCommand(
                    _customerId, BasicsStep.Name.Trim(),
                    BasicsStep.SelectedTypeOption.Value,
                    string.IsNullOrWhiteSpace(BasicsStep.Description) ? null : BasicsStep.Description.Trim(),
                    parsedTenantId, domain, workloadAreas));
            }

            try
            {
                AppServiceLocator.Get<AppShellViewModel>().ShowToast(
                    IsEditMode ? "Environment Updated" : "Environment Created",
                    $"'{BasicsStep.Name.Trim()}' has been saved successfully.",
                    NotificationType.Success);
            }
            catch { /* toast is optional */ }

            Completed?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke();

    private bool CurrentStepCanProceed() => CurrentStep switch
    {
        0 => !string.IsNullOrWhiteSpace(BasicsStep.Name),
        1 => WorkloadStep.HasSelection,
        2 => true, // Prerequisites are advisory
        _ => true
    };
}

/// <summary>Step 1: Environment basics (name, type, description, tenant ID, domain).</summary>
public sealed partial class EnvironmentBasicsStepViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private EnvironmentTypeOption _selectedTypeOption = EnvironmentTypeOption.All[0];

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _tenantDomain = string.Empty;

    public IReadOnlyList<EnvironmentTypeOption> AvailableTypes => EnvironmentTypeOption.All;
}
