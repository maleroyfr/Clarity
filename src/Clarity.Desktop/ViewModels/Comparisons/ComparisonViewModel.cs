using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Comparisons;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Environments.Queries;
using Clarity.Application.Snapshots;
using Clarity.Application.Snapshots.Queries;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Comparisons;

/// <summary>Picker item wrapping a snapshot for ComboBox display.</summary>
public sealed class SnapshotPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public override string ToString() => $"{Name} ({EnvironmentName})";
}

/// <summary>Display wrapper for the <see cref="ComparisonMode"/> enum.</summary>
public sealed class ComparisonModeOption
{
    public ComparisonMode Mode { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>Selectable customer item for the customer picker.</summary>
public sealed class CustomerPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

/// <summary>Main ViewModel for the Comparisons page.</summary>
public sealed partial class ComparisonViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    // ── Customer picker ──────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<CustomerPickerItem> _customers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    private CustomerPickerItem? _selectedCustomer;

    [ObservableProperty]
    private bool _isLoadingCustomers;

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    // ── Snapshot pickers ─────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<SnapshotPickerItem> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunComparison))]
    private SnapshotPickerItem? _selectedLeftSnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunComparison))]
    private SnapshotPickerItem? _selectedRightSnapshot;

    [ObservableProperty]
    private bool _isLoadingSnapshots;

    // ── Comparison mode ──────────────────────────────────────────────────

    public ObservableCollection<ComparisonModeOption> ComparisonModes { get; } =
    [
        new() { Mode = ComparisonMode.SnapshotOverTime, DisplayName = "Snapshot Over Time" },
        new() { Mode = ComparisonMode.CrossTenant, DisplayName = "Cross-Tenant" },
        new() { Mode = ComparisonMode.MigrationAnalysis, DisplayName = "Migration Analysis" },
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunComparison))]
    private ComparisonModeOption? _selectedMode;

    // ── Run state ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunComparison))]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    // ── Results ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private ComparisonJobDto? _lastResult;

    [ObservableProperty]
    private int _totalLeft;

    [ObservableProperty]
    private int _totalRight;

    [ObservableProperty]
    private int _added;

    [ObservableProperty]
    private int _removed;

    [ObservableProperty]
    private int _modified;

    [ObservableProperty]
    private int _unchanged;

    [ObservableProperty]
    private ObservableCollection<WorkloadAreaSummaryDto> _workloadBreakdown = [];

    public bool HasResults => LastResult?.Summary is not null;

    public bool CanRunComparison =>
        !IsRunning
        && SelectedLeftSnapshot is not null
        && SelectedRightSnapshot is not null
        && SelectedMode is not null;

    // ── Constructor ──────────────────────────────────────────────────────

    public ComparisonViewModel(IMediator mediator)
    {
        _mediator = mediator;
        SelectedMode = ComparisonModes[0];
    }

    // ── Customer loading ─────────────────────────────────────────────────

    public async Task LoadCustomersAsync()
    {
        IsLoadingCustomers = true;
        ErrorMessage = null;
        try
        {
            var dtos = await _mediator.Send(new ListCustomersQuery(IncludeArchived: false));
            Customers = new ObservableCollection<CustomerPickerItem>(
                dtos.Select(c => new CustomerPickerItem { Id = c.Id, Name = c.Name }));

            if (Customers.Count == 1)
                SelectedCustomer = Customers[0];
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load customers: {ex.Message}";
        }
        finally
        {
            IsLoadingCustomers = false;
        }
    }

    partial void OnSelectedCustomerChanged(CustomerPickerItem? value)
    {
        if (value is not null)
            _ = LoadSnapshotsAsync(value.Id);
        else
        {
            Snapshots = [];
            SelectedLeftSnapshot = null;
            SelectedRightSnapshot = null;
        }
    }

    // ── Snapshot loading (all envs → all snapshots) ──────────────────────

    private async Task LoadSnapshotsAsync(Guid customerId)
    {
        IsLoadingSnapshots = true;
        ErrorMessage = null;
        SelectedLeftSnapshot = null;
        SelectedRightSnapshot = null;

        try
        {
            var environments = await _mediator.Send(
                new ListEnvironmentsByCustomerQuery(customerId));

            var items = new List<SnapshotPickerItem>();
            foreach (var env in environments)
            {
                var snaps = await _mediator.Send(
                    new ListSnapshotsByEnvironmentQuery(env.Id));

                items.AddRange(snaps.Select(s => new SnapshotPickerItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    EnvironmentName = env.Name,
                }));
            }

            Snapshots = new ObservableCollection<SnapshotPickerItem>(
                items.OrderBy(i => i.EnvironmentName).ThenBy(i => i.Name));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load snapshots: {ex.Message}";
        }
        finally
        {
            IsLoadingSnapshots = false;
        }
    }

    // ── Run comparison ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunComparison))]
    public async Task RunComparisonAsync()
    {
        if (SelectedCustomer is null || SelectedLeftSnapshot is null
            || SelectedRightSnapshot is null || SelectedMode is null)
            return;

        IsRunning = true;
        ErrorMessage = null;
        LastResult = null;

        try
        {
            var name = $"Compare_{SelectedLeftSnapshot.Name}_{SelectedRightSnapshot.Name}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";

            var result = await _mediator.Send(new RunComparisonCommand(
                SelectedCustomer.Id,
                name,
                SelectedMode.Mode,
                SelectedLeftSnapshot.Id,
                SelectedRightSnapshot.Id,
                WorkloadFilter: null));

            LastResult = result;

            if (result.Summary is { } summary)
            {
                TotalLeft = summary.TotalLeft;
                TotalRight = summary.TotalRight;
                Added = summary.Added;
                Removed = summary.Removed;
                Modified = summary.Modified;
                Unchanged = summary.Unchanged;
                WorkloadBreakdown = new ObservableCollection<WorkloadAreaSummaryDto>(summary.ByWorkload);
            }

            if (result.Status == JobStatus.Failed)
                ErrorMessage = "Comparison completed with failures. Check the results for details.";
            else
                AppServiceLocator.Get<AppShellViewModel>().ShowToast("Comparison Complete",
                    $"+{Added} added, −{Removed} removed, ~{Modified} modified", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Comparison failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
