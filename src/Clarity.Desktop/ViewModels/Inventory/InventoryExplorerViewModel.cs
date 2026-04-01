using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Inventory;
using Clarity.Desktop.Services;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Inventory;

/// <summary>Category + count pair for snapshot summary display.</summary>
public sealed record CategoryCountVm(string Category, string Icon, int Count);

/// <summary>Per-row ViewModel for each snapshot summary card.</summary>
public sealed partial class SnapshotSummaryItemVm : ObservableObject
{
    private readonly Action<SnapshotSummaryItemVm> _onDrillInto;

    public Guid SnapshotId { get; }
    public string SnapshotName { get; }
    public int TotalObjects { get; }
    public IReadOnlyList<CategoryCountVm> CategorySummaries { get; }

    public SnapshotSummaryItemVm(
        SnapshotInventorySummaryDto dto,
        Action<SnapshotSummaryItemVm> onDrillInto)
    {
        _onDrillInto = onDrillInto;
        SnapshotId = dto.SnapshotId;
        SnapshotName = dto.SnapshotName;
        TotalObjects = dto.TotalObjects;
        CategorySummaries = dto.TypeSummaries
            .GroupBy(ts => ts.Category)
            .Select(g => new CategoryCountVm(g.Key, GetCategoryIcon(g.Key), g.Sum(ts => ts.Count)))
            .OrderByDescending(c => c.Count)
            .ToList();
    }

    [RelayCommand]
    public void DrillInto() => _onDrillInto(this);

    private static string GetCategoryIcon(string category) => category switch
    {
        "Entra ID" => "🔐",
        "Tenant" => "🏛",
        "Licenses" => "📜",
        "Intune" => "📱",
        "Exchange Online" => "📧",
        "SharePoint" => "📁",
        "Teams" => "💬",
        "Active Directory" => "🏗",
        _ => "📦"
    };
}

/// <summary>Main entry-point page for the Inventory Explorer.</summary>
public sealed partial class InventoryExplorerViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    private List<SnapshotInventorySummaryDto> _allSnapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    private ObservableCollection<SnapshotSummaryItemVm> _filteredSnapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    private ObservableObject? _detailPage;

    public bool IsEmpty => !IsLoading && FilteredSnapshots.Count == 0;
    public bool HasSnapshots => !IsLoading && FilteredSnapshots.Count > 0;
    public bool ShowDetail => DetailPage is not null;

    public InventoryExplorerViewModel(IMediator mediator)
    {
        _mediator = mediator;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allSnapshots = (await _mediator.Send(
                new ListSnapshotsWithInventorySummaryQuery())).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load inventory snapshots: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSnapshots
            : _allSnapshots.Where(s =>
                s.SnapshotName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredSnapshots = new ObservableCollection<SnapshotSummaryItemVm>(
            filtered.Select(d => new SnapshotSummaryItemVm(d, OnDrillInto)));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasSnapshots));
    }

    private void OnDrillInto(SnapshotSummaryItemVm item)
    {
        var objectList = AppServiceLocator.Get<InventoryObjectListViewModel>();
        objectList.SetContext(item.SnapshotId, item.SnapshotName);
        objectList.BackRequested += OnBackRequested;
        DetailPage = objectList;
    }

    [RelayCommand]
    public void DrillIntoWithType(SnapshotSummaryItemVm item)
    {
        var objectList = AppServiceLocator.Get<InventoryObjectListViewModel>();
        objectList.SetContext(item.SnapshotId, item.SnapshotName);
        objectList.BackRequested += OnBackRequested;
        DetailPage = objectList;
    }

    [RelayCommand]
    public void BackToList()
    {
        if (DetailPage is InventoryObjectListViewModel objectList)
            objectList.BackRequested -= OnBackRequested;

        DetailPage = null;
    }

    private void OnBackRequested() => BackToList();
}
