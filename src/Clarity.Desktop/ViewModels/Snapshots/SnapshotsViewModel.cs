using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Queries;
using Clarity.Application.Snapshots;
using Clarity.Application.Snapshots.Commands;
using Clarity.Application.Snapshots.Queries;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Snapshots;

/// <summary>Per-row ViewModel for a snapshot in the list.</summary>
public sealed partial class SnapshotListItemVm : ObservableObject
{
    private readonly SnapshotDto _dto;
    private readonly Func<SnapshotDto, Task> _onSeal;
    private readonly Func<Guid, Task> _onDelete;
    private readonly Action<Guid> _onViewInventory;
    private readonly Action<Guid, string> _onExport;

    public Guid Id { get; }
    public string Name { get; }
    public SnapshotStatus Status { get; }
    public string StatusDisplay => Status.ToString();
    public bool IsImmutable { get; }
    public DateTimeOffset CreatedAt { get; }
    public int WorkloadScopeCount { get; }
    public int CollectorRunCount { get; }
    public bool CanSeal => Status is SnapshotStatus.Draft or SnapshotStatus.Completed;

    public bool IsDraft => Status == SnapshotStatus.Draft;
    public bool IsRunningOrPartial => Status is SnapshotStatus.Running or SnapshotStatus.Partial;
    public bool IsCompleted => Status == SnapshotStatus.Completed;
    public bool IsFailed => Status == SnapshotStatus.Failed;

    public string StatusColor => Status switch
    {
        SnapshotStatus.Completed => "#4CAF50",
        SnapshotStatus.Running or SnapshotStatus.Partial => "#FF9800",
        SnapshotStatus.Failed => "#F44336",
        _ => "#9E9E9E"
    };

    public SnapshotListItemVm(
        SnapshotDto dto,
        Func<SnapshotDto, Task> onSeal,
        Func<Guid, Task> onDelete,
        Action<Guid> onViewInventory,
        Action<Guid, string> onExport)
    {
        _dto = dto;
        _onSeal = onSeal;
        _onDelete = onDelete;
        _onViewInventory = onViewInventory;
        _onExport = onExport;

        Id = dto.Id;
        Name = dto.Name;
        Status = dto.Status;
        IsImmutable = dto.IsImmutable;
        CreatedAt = dto.CreatedAt;
        WorkloadScopeCount = dto.WorkloadScope.Count;
        CollectorRunCount = dto.CollectorRunCount;
    }

    [RelayCommand]
    public async Task Seal() => await _onSeal(_dto);

    [RelayCommand]
    public async Task Delete() => await _onDelete(Id);

    [RelayCommand]
    public void ViewInventory() => _onViewInventory(Id);

    [RelayCommand]
    public void Export() => _onExport(Id, Name);
}

/// <summary>Selectable customer item for the customer picker.</summary>
public sealed class CustomerPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>Selectable environment item for the environment picker.</summary>
public sealed class EnvironmentPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> WorkloadAreas { get; init; } = [];

    public override string ToString() => Name;
}

public sealed partial class SnapshotsViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    // ── Customer picker ──────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<CustomerPickerItem> _customers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(HasSelectedEnvironment))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private CustomerPickerItem? _selectedCustomer;

    [ObservableProperty]
    private bool _isLoadingCustomers;

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    // ── Environment picker ───────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<EnvironmentPickerItem> _environments = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEnvironment))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private EnvironmentPickerItem? _selectedEnvironment;

    [ObservableProperty]
    private bool _isLoadingEnvironments;

    public bool HasSelectedEnvironment => SelectedCustomer is not null && SelectedEnvironment is not null;

    // ── Snapshot list ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    private ObservableCollection<SnapshotListItemVm> _filteredSnapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    public bool IsEmpty => !IsLoading && HasSelectedEnvironment && FilteredSnapshots.Count == 0;
    public bool HasSnapshots => !IsLoading && FilteredSnapshots.Count > 0;

    private List<SnapshotDto> _allSnapshots = [];

    // ── Events ───────────────────────────────────────────────────────────
    public event Action<Guid>? ViewInventoryRequested;
    public event Action<Guid, string>? ExportRequested;

    public SnapshotsViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── Customer loading ─────────────────────────────────────────────────

    /// <summary>Loads the customer list for the picker.</summary>
    public async Task LoadCustomersAsync()
    {
        IsLoadingCustomers = true;
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
        SelectedEnvironment = null;
        _allSnapshots = [];
        FilteredSnapshots = [];

        if (value is not null)
            _ = LoadEnvironmentsAsync();
        else
        {
            Environments = [];
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasSnapshots));
        }
    }

    // ── Environment loading ──────────────────────────────────────────────

    private async Task LoadEnvironmentsAsync()
    {
        if (SelectedCustomer is null) return;

        IsLoadingEnvironments = true;
        try
        {
            var dtos = await _mediator.Send(
                new ListEnvironmentsByCustomerQuery(SelectedCustomer.Id, IncludeArchived: false));
            Environments = new ObservableCollection<EnvironmentPickerItem>(
                dtos.Select(e => new EnvironmentPickerItem
                {
                    Id = e.Id,
                    Name = e.Name,
                    WorkloadAreas = e.WorkloadAreas
                }));

            if (Environments.Count == 1)
                SelectedEnvironment = Environments[0];
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load environments: {ex.Message}";
        }
        finally
        {
            IsLoadingEnvironments = false;
        }
    }

    partial void OnSelectedEnvironmentChanged(EnvironmentPickerItem? value)
    {
        if (value is not null)
            _ = LoadSnapshotsAsync();
        else
        {
            _allSnapshots = [];
            FilteredSnapshots = [];
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasSnapshots));
        }
    }

    // ── Snapshot loading ─────────────────────────────────────────────────

    private async Task LoadSnapshotsAsync()
    {
        if (SelectedEnvironment is null) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allSnapshots = (await _mediator.Send(
                new ListSnapshotsByEnvironmentQuery(SelectedEnvironment.Id))).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load snapshots: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Cross-navigation entry point ─────────────────────────────────────

    /// <summary>
    /// Called by AppShellViewModel when navigating from another page with
    /// a pre-selected customer and environment context.
    /// </summary>
    public async Task LoadForEnvironmentAsync(Guid customerId, Guid envId, string envName)
    {
        await LoadCustomersAsync();

        var customerMatch = Customers.FirstOrDefault(c => c.Id == customerId);
        if (customerMatch is null) return;
        SelectedCustomer = customerMatch;

        // Wait for environments to load before selecting
        await LoadEnvironmentsAsync();

        var envMatch = Environments.FirstOrDefault(e => e.Id == envId);
        if (envMatch is not null)
            SelectedEnvironment = envMatch;
    }

    // ── Search / filter ──────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSnapshots
            : _allSnapshots.Where(s =>
                s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredSnapshots = new ObservableCollection<SnapshotListItemVm>(
            filtered.Select(d => new SnapshotListItemVm(d, OnSealItem, OnDeleteItem, OnViewInventory, OnExport)));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasSnapshots));
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasSelectedEnvironment))]
    public async Task CreateSnapshot()
    {
        if (SelectedCustomer is null || SelectedEnvironment is null) return;

        IsCreating = true;
        ErrorMessage = null;
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var name = $"Snapshot_{SelectedEnvironment.Name}_{timestamp}";

            var workloadScope = SelectedEnvironment.WorkloadAreas
                .Select(w => Enum.Parse<WorkloadArea>(w, ignoreCase: true))
                .ToList();

            await _mediator.Send(new CreateSnapshotCommand(
                SelectedCustomer.Id,
                SelectedEnvironment.Id,
                name,
                workloadScope,
                Description: null));

            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create snapshot: {ex.Message}";
        }
        finally
        {
            IsCreating = false;
        }
    }

    private async Task OnSealItem(SnapshotDto dto)
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new SealSnapshotCommand(dto.Id));
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to seal snapshot: {ex.Message}";
        }
    }

    private async Task OnDeleteItem(Guid snapshotId)
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new DeleteSnapshotCommand(snapshotId));
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete snapshot: {ex.Message}";
        }
    }

    private void OnViewInventory(Guid snapshotId)
        => ViewInventoryRequested?.Invoke(snapshotId);

    private void OnExport(Guid snapshotId, string snapshotName)
        => ExportRequested?.Invoke(snapshotId, snapshotName);
}
