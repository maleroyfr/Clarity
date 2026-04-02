using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Commands;
using Clarity.Application.Customers.Queries;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Customers;

/// <summary>Per-row ViewModel for the customer list — encapsulates item-level commands.</summary>
public sealed partial class CustomerListItemVm : ObservableObject
{
    private readonly Action<CustomerDto> _onEdit;
    private readonly Action<CustomerDto> _onViewEnvironments;
    private readonly Func<CustomerDto, Task> _onArchive;
    private readonly Func<CustomerDto, Task> _onRestore;

    public Guid Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool IsArchived { get; }
    public DateTimeOffset CreatedAt { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string CreatedAtDisplay => $"Created {CreatedAt:MMM d, yyyy}";

    private CustomerDto _dto;

    public CustomerListItemVm(
        CustomerDto dto,
        Action<CustomerDto> onEdit,
        Func<CustomerDto, Task> onArchive,
        Func<CustomerDto, Task> onRestore,
        Action<CustomerDto> onViewEnvironments)
    {
        _dto = dto;
        Id = dto.Id;
        Name = dto.Name;
        Description = dto.Description;
        IsArchived = dto.IsArchived;
        CreatedAt = dto.CreatedAt;
        _onEdit = onEdit;
        _onArchive = onArchive;
        _onRestore = onRestore;
        _onViewEnvironments = onViewEnvironments;
    }

    [RelayCommand]
    public void Edit() => _onEdit(_dto);

    [RelayCommand]
    public void ViewEnvironments() => _onViewEnvironments(_dto);

    [RelayCommand]
    public async Task Archive() => await _onArchive(_dto);

    [RelayCommand]
    public async Task Restore() => await _onRestore(_dto);
}

public sealed partial class CustomersListViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    private ObservableCollection<CustomerListItemVm> _filteredCustomers = [];

    [ObservableProperty]
    private CustomerListItemVm? _selectedCustomer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasCustomers))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasCustomers))]
    private bool _showArchived;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public bool IsEmpty => !IsLoading && FilteredCustomers.Count == 0;
    public bool HasCustomers => !IsLoading && FilteredCustomers.Count > 0;

    private List<CustomerDto> _allCustomers = [];

    public event Action<CustomerDto?>? EditRequested;
    public event Action<Guid>? ViewEnvironmentsRequested;

    public CustomersListViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allCustomers = (await _mediator.Send(new ListCustomersQuery(ShowArchived))).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load customers: {ex.Message}";
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
            ? _allCustomers
            : _allCustomers.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredCustomers = new ObservableCollection<CustomerListItemVm>(
            filtered.Select(d => new CustomerListItemVm(d, OnEditItem, OnArchiveItem, OnRestoreItem, OnViewEnvironments)));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasCustomers));
    }

    private void OnEditItem(CustomerDto dto) => EditRequested?.Invoke(dto);

    private void OnViewEnvironments(CustomerDto dto) => ViewEnvironmentsRequested?.Invoke(dto.Id);

    private async Task OnArchiveItem(CustomerDto dto)
    {
        try
        {
            await _mediator.Send(new ArchiveCustomerCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to archive: {ex.Message}";
        }
    }

    private async Task OnRestoreItem(CustomerDto dto)
    {
        try
        {
            await _mediator.Send(new RestoreCustomerCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to restore: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CreateNew() => EditRequested?.Invoke(null);
}
