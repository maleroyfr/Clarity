using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Commands;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Commands;
using Clarity.Application.Environments.Queries;
using Clarity.Domain.Environments;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Environments;

/// <summary>Per-row ViewModel for the environments list.</summary>
public sealed partial class EnvironmentListItemVm : ObservableObject
{
    private readonly Action<EnvironmentDto> _onEdit;
    private readonly Action<EnvironmentDto> _onConfigureAuth;
    private readonly Func<EnvironmentDto, Task> _onArchive;
    private readonly Func<EnvironmentDto, Task> _onDelete;
    private readonly EnvironmentDto _dto;

    public Guid Id { get; }
    public string Name { get; }
    public EnvironmentType Type { get; }
    public EnvironmentStatus Status { get; }
    public string? TenantDomain { get; }
    public bool IsArchived { get; }
    public int WorkloadCount { get; }

    public string TypeDisplay => EnvironmentTypeHelper.ToDisplayName(Type);
    public string StatusDisplay => Status.ToString();

    public EnvironmentListItemVm(
        EnvironmentDto dto,
        Action<EnvironmentDto> onEdit,
        Action<EnvironmentDto> onConfigureAuth,
        Func<EnvironmentDto, Task> onArchive,
        Func<EnvironmentDto, Task> onDelete)
    {
        _dto = dto;
        Id = dto.Id;
        Name = dto.Name;
        Type = dto.Type;
        Status = dto.Status;
        TenantDomain = dto.TenantDomain;
        IsArchived = dto.IsArchived;
        WorkloadCount = dto.WorkloadAreas.Count;
        _onEdit = onEdit;
        _onConfigureAuth = onConfigureAuth;
        _onArchive = onArchive;
        _onDelete = onDelete;
    }

    [RelayCommand]
    public void Edit() => _onEdit(_dto);

    [RelayCommand]
    public void ConfigureAuth() => _onConfigureAuth(_dto);

    [RelayCommand]
    public async Task Archive() => await _onArchive(_dto);

    [RelayCommand]
    public async Task Delete() => await _onDelete(_dto);
}

/// <summary>Selectable customer item for the customer picker.</summary>
public sealed class CustomerPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

public sealed partial class EnvironmentsListViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    // ── Customer picker ──────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<CustomerPickerItem> _customers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private CustomerPickerItem? _selectedCustomer;

    [ObservableProperty]
    private bool _isLoadingCustomers;

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    // ── Environments list ────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasEnvironments))]
    private ObservableCollection<EnvironmentListItemVm> _filteredEnvironments = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasEnvironments))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasEnvironments))]
    private bool _showArchived;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public bool IsEmpty => !IsLoading && HasSelectedCustomer && FilteredEnvironments.Count == 0;
    public bool HasEnvironments => !IsLoading && FilteredEnvironments.Count > 0;

    private List<EnvironmentDto> _allEnvironments = [];

    public event Action<EnvironmentDto?>? EditRequested;
    public event Action<EnvironmentDto>? ConfigureAuthRequested;

    public EnvironmentsListViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Loads the customer list for the picker.</summary>
    public async Task LoadCustomersAsync()
    {
        IsLoadingCustomers = true;
        try
        {
            var dtos = await _mediator.Send(new ListCustomersQuery(IncludeArchived: false));
            Customers = new ObservableCollection<CustomerPickerItem>(
                dtos.Select(c => new CustomerPickerItem { Id = c.Id, Name = c.Name }));

            // Auto-select if only one customer
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
            _ = LoadAsync();
        else
        {
            _allEnvironments = [];
            FilteredEnvironments = [];
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasEnvironments));
        }
    }

    /// <summary>Sets the customer context and loads environments.</summary>
    public void SetCustomer(Guid customerId)
    {
        var match = Customers.FirstOrDefault(c => c.Id == customerId);
        if (match is not null)
            SelectedCustomer = match;
    }

    public async Task LoadAsync()
    {
        if (SelectedCustomer is null) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allEnvironments = (await _mediator.Send(
                new ListEnvironmentsByCustomerQuery(SelectedCustomer.Id, ShowArchived))).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load environments: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowArchivedChanged(bool value) => _ = LoadAsync();

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allEnvironments
            : _allEnvironments.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredEnvironments = new ObservableCollection<EnvironmentListItemVm>(
            filtered.Select(d => new EnvironmentListItemVm(d, OnEditItem, OnConfigureAuthItem, OnArchiveItem, OnDeleteItem)));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEnvironments));
    }

    private void OnEditItem(EnvironmentDto dto) => EditRequested?.Invoke(dto);

    private void OnConfigureAuthItem(EnvironmentDto dto) => ConfigureAuthRequested?.Invoke(dto);

    private async Task OnArchiveItem(EnvironmentDto dto)
    {
        try
        {
            await _mediator.Send(new ArchiveEnvironmentCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to archive: {ex.Message}";
        }
    }

    private async Task OnDeleteItem(EnvironmentDto dto)
    {
        try
        {
            await _mediator.Send(new DeleteEnvironmentCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedCustomer))]
    public void CreateNew() => EditRequested?.Invoke(null);
}
