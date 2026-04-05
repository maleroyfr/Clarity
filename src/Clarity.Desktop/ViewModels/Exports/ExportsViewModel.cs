using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Commands;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Queries;
using Clarity.Application.Exports;
using Clarity.Application.Snapshots;
using Clarity.Application.Snapshots.Queries;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using Clarity.SharedContracts.Enums;
using MediatR;

namespace Clarity.Desktop.ViewModels.Exports;

public sealed partial class ExportHistoryItem
{
    public Guid JobId { get; init; }
    public string SnapshotName { get; init; } = string.Empty;
    public ExportFormat Format { get; init; }
    public JobStatus Status { get; init; }
    public string? OutputPath { get; init; }
    public long BytesWritten { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExportedAt { get; init; }

    public string FormatDisplay => Format.ToString().ToUpperInvariant();
    public string StatusDisplay => Status.ToString();
    public string SizeDisplay => BytesWritten switch
    {
        >= 1_048_576 => $"{BytesWritten / 1_048_576.0:F1} MB",
        >= 1_024 => $"{BytesWritten / 1_024.0:F1} KB",
        _ => $"{BytesWritten} B"
    };
    public bool IsSuccess => Status == JobStatus.Completed;
    public bool IsFailed => Status == JobStatus.Failed;
}

public sealed partial class ExportsViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    public ExportsViewModel(IMediator mediator)
    {
        _mediator = mediator;
        _outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clarity",
            "exports");
    }

    // --- Customers ---

    [ObservableProperty]
    private ObservableCollection<CustomerDto> _customers = [];

    [ObservableProperty]
    private CustomerDto? _selectedCustomer;

    // --- Snapshots ---

    [ObservableProperty]
    private ObservableCollection<SnapshotDto> _snapshots = [];

    [ObservableProperty]
    private SnapshotDto? _selectedSnapshot;

    [ObservableProperty]
    private bool _isLoadingSnapshots;

    // --- Format ---

    [ObservableProperty]
    private ExportFormat _selectedFormat = ExportFormat.Csv;

    public ExportFormat[] AvailableFormats { get; } =
        [ExportFormat.Csv, ExportFormat.Xlsx, ExportFormat.Json];

    // --- Output ---

    [ObservableProperty]
    private string _outputPath;

    // --- State ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _isExporting;

    // --- Result ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(IsResultSuccess))]
    [NotifyPropertyChangedFor(nameof(IsResultError))]
    [NotifyPropertyChangedFor(nameof(ResultFilePath))]
    [NotifyPropertyChangedFor(nameof(ResultSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultErrorMessage))]
    private ExportJobDto? _lastResult;

    public bool HasResult => LastResult is not null;
    public bool IsResultSuccess => LastResult?.Status == JobStatus.Completed;
    public bool IsResultError => LastResult?.Status == JobStatus.Failed;
    public string? ResultFilePath => LastResult?.OutputPath;
    public string ResultSizeDisplay => LastResult switch
    {
        { BytesWritten: >= 1_048_576 } r => $"{r.BytesWritten / 1_048_576.0:F1} MB",
        { BytesWritten: >= 1_024 } r => $"{r.BytesWritten / 1_024.0:F1} KB",
        { } r => $"{r.BytesWritten} B",
        _ => string.Empty
    };
    public string? ResultErrorMessage => LastResult?.ErrorMessage;

    // --- History ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    private ObservableCollection<ExportHistoryItem> _exportHistory = [];

    public bool HasHistory => ExportHistory.Count > 0;

    // --- Computed ---

    public bool IsEmpty => !IsLoading && Customers.Count == 0;
    public bool CanExport => !IsExporting && SelectedSnapshot is not null;

    // --- Events ---

    /// <summary>Raised when the user clicks Browse; the code-behind opens a file dialog.</summary>
    public event Action? BrowseRequested;

    // --- Cascade: customer → snapshots ---

    partial void OnSelectedCustomerChanged(CustomerDto? value)
    {
        SelectedSnapshot = null;
        Snapshots.Clear();
        if (value is not null)
            _ = LoadSnapshotsAsync(value.Id);
    }

    partial void OnSelectedSnapshotChanged(SnapshotDto? value)
    {
        OnPropertyChanged(nameof(CanExport));
    }

    // --- Public API ---

    public async Task LoadCustomersAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var list = await _mediator.Send(new ListCustomersQuery());
            Customers = new ObservableCollection<CustomerDto>(list);
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

    /// <summary>
    /// Called from cross-navigation to pre-select a snapshot by ID and name.
    /// </summary>
    public void SetSnapshotContext(Guid snapshotId, string snapshotName)
    {
        var existing = Snapshots.FirstOrDefault(s => s.Id == snapshotId);
        if (existing is not null)
        {
            SelectedSnapshot = existing;
            return;
        }

        // Try to find via customer match after snapshots are loaded
        _ = TrySelectSnapshotAsync(snapshotId);
    }

    // --- Private helpers ---

    private async Task LoadSnapshotsAsync(Guid customerId)
    {
        IsLoadingSnapshots = true;
        ErrorMessage = null;
        try
        {
            var environments = await _mediator.Send(new ListEnvironmentsByCustomerQuery(customerId));
            var all = new List<SnapshotDto>();
            foreach (var env in environments)
            {
                var snaps = await _mediator.Send(new ListSnapshotsByEnvironmentQuery(env.Id));
                all.AddRange(snaps);
            }

            Snapshots = new ObservableCollection<SnapshotDto>(
                all.OrderByDescending(s => s.CreatedAt));
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

    private async Task TrySelectSnapshotAsync(Guid snapshotId)
    {
        // If customers are loaded, search across them
        foreach (var customer in Customers)
        {
            var environments = await _mediator.Send(new ListEnvironmentsByCustomerQuery(customer.Id));
            foreach (var env in environments)
            {
                var snaps = await _mediator.Send(new ListSnapshotsByEnvironmentQuery(env.Id));
                var match = snaps.FirstOrDefault(s => s.Id == snapshotId);
                if (match is not null)
                {
                    SelectedCustomer = customer;
                    // Wait for cascade load to finish, then select
                    await Task.Delay(100);
                    SelectedSnapshot = Snapshots.FirstOrDefault(s => s.Id == snapshotId) ?? match;
                    return;
                }
            }
        }
    }

    // --- Commands ---

    [RelayCommand]
    private void Browse() => BrowseRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedSnapshot is null) return;

        IsExporting = true;
        ErrorMessage = null;
        LastResult = null;

        try
        {
            var result = await _mediator.Send(new CreateExportJobCommand(
                SelectedSnapshot.Id,
                SelectedFormat,
                OutputPath,
                WorkloadFilter: null));

            LastResult = result;

            if (result.Status == JobStatus.Completed)
                AppServiceLocator.Get<AppShellViewModel>().ShowToast("Export Complete", $"{SelectedFormat.ToString().ToUpperInvariant()} export saved to {result.OutputPath}", NotificationType.Success);
            else if (result.Status == JobStatus.Failed)
                AppServiceLocator.Get<AppShellViewModel>().ShowToast("Export Failed", result.ErrorMessage ?? "Unknown error", NotificationType.Error);

            ExportHistory.Insert(0, new ExportHistoryItem
            {
                JobId = result.Id,
                SnapshotName = SelectedSnapshot.Name,
                Format = SelectedFormat,
                Status = result.Status,
                OutputPath = result.OutputPath,
                BytesWritten = result.BytesWritten,
                ErrorMessage = result.ErrorMessage,
                ExportedAt = DateTimeOffset.Now
            });
            OnPropertyChanged(nameof(HasHistory));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (string.IsNullOrWhiteSpace(LastResult?.OutputPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LastResult.OutputPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open file: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenHistoryFile(ExportHistoryItem? item)
    {
        if (string.IsNullOrWhiteSpace(item?.OutputPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.OutputPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open file: {ex.Message}";
        }
    }
}
