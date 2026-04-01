using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Inventory;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Inventory;

/// <summary>Option for the type filter dropdown.</summary>
public sealed record TypeFilterOption(InventoryObjectType? Type, string DisplayName, int Count);

/// <summary>Per-row ViewModel for each inventory object.</summary>
public sealed partial class InventoryObjectItemVm : ObservableObject
{
    private readonly Action<InventoryObjectItemVm> _onSelect;

    public Guid Id { get; }
    public InventoryObjectType ObjectType { get; }
    public string TypeDisplay { get; }
    public string ExternalId { get; }
    public string? DisplayName { get; }
    public IReadOnlyList<KeyValuePair<string, string?>> KeyProperties { get; }

    public InventoryObjectItemVm(
        InventoryObjectDto dto,
        Action<InventoryObjectItemVm> onSelect)
    {
        _onSelect = onSelect;
        Id = dto.Id;
        ObjectType = dto.ObjectType;
        TypeDisplay = InventoryTypeCategories.GetDisplayName(dto.ObjectType);
        ExternalId = dto.ExternalId;
        DisplayName = dto.DisplayName;
        KeyProperties = dto.Properties
            .Where(kv => kv.Value is not null)
            .Take(4)
            .ToList();
    }

    [RelayCommand]
    public void Select() => _onSelect(this);
}

/// <summary>Filterable list of inventory objects for a given snapshot.</summary>
public sealed partial class InventoryObjectListViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid _snapshotId;

    private List<InventoryObjectDto> _allObjects = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasObjects))]
    private ObservableCollection<InventoryObjectItemVm> _filteredObjects = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasObjects))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private InventoryObjectType? _selectedTypeFilter;

    [ObservableProperty]
    private string _snapshotName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    private InventoryObjectItemVm? _selectedObject;

    [ObservableProperty]
    private ObservableCollection<TypeFilterOption> _availableTypes = [];

    public bool IsEmpty => !IsLoading && FilteredObjects.Count == 0;
    public bool HasObjects => !IsLoading && FilteredObjects.Count > 0;
    public bool ShowDetail => SelectedObject is not null;

    public event Action? BackRequested;

    public InventoryObjectListViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Sets the snapshot context and triggers loading.</summary>
    public void SetContext(Guid snapshotId, string snapshotName, InventoryObjectType? initialTypeFilter = null)
    {
        _snapshotId = snapshotId;
        SnapshotName = snapshotName;
        SelectedTypeFilter = initialTypeFilter;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allObjects = (await _mediator.Send(
                new ListInventoryObjectsQuery(_snapshotId))).ToList();
            BuildAvailableTypes();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load inventory objects: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(InventoryObjectType? value) => ApplyFilter();

    private void BuildAvailableTypes()
    {
        var typeCounts = _allObjects
            .GroupBy(o => o.ObjectType)
            .Select(g => new TypeFilterOption(g.Key, InventoryTypeCategories.GetDisplayName(g.Key), g.Count()))
            .OrderBy(t => t.DisplayName)
            .ToList();

        var options = new List<TypeFilterOption>
        {
            new(null, "All Types", _allObjects.Count)
        };
        options.AddRange(typeCounts);

        AvailableTypes = new ObservableCollection<TypeFilterOption>(options);
    }

    private void ApplyFilter()
    {
        var filtered = _allObjects.AsEnumerable();

        if (SelectedTypeFilter is not null)
            filtered = filtered.Where(o => o.ObjectType == SelectedTypeFilter);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(o =>
                (o.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                o.ExternalId.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        FilteredObjects = new ObservableCollection<InventoryObjectItemVm>(
            filtered.Select(d => new InventoryObjectItemVm(d, OnSelectItem)));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasObjects));
    }

    private void OnSelectItem(InventoryObjectItemVm item) => SelectedObject = item;

    [RelayCommand]
    public void ClearSelection() => SelectedObject = null;

    [RelayCommand]
    public void Back() => BackRequested?.Invoke();
}
