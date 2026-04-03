using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Inventory;
using Clarity.Application.Snapshots;
using Clarity.Application.Snapshots.Queries;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using MediatR;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    private int _totalCustomers;

    [ObservableProperty]
    private int _totalEnvironments;

    [ObservableProperty]
    private int _totalSnapshots;

    [ObservableProperty]
    private int _totalInventoryObjects;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    public string WelcomeMessage { get; } = "Welcome to Clarity";
    public string SubMessage { get; } = "Audit & Discovery Platform for Microsoft 365 and Active Directory";

    /// <summary>Fired when a quick-action card is clicked (section to navigate to).</summary>
    public event Action<NavSection>? NavigateRequested;

    public HomeViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var customers = await _mediator.Send(new ListCustomersQuery(IncludeArchived: false));
            TotalCustomers = customers.Count;

            int envCount = 0;
            int snapCount = 0;
            int objCount = 0;
            foreach (var c in customers)
            {
                var envs = await _mediator.Send(
                    new Clarity.Application.Environments.Queries.ListEnvironmentsByCustomerQuery(c.Id, false));
                envCount += envs.Count;

                foreach (var env in envs)
                {
                    var snapshots = await _mediator.Send(new ListSnapshotsByEnvironmentQuery(env.Id));
                    snapCount += snapshots.Count;
                }
            }
            TotalEnvironments = envCount;
            TotalSnapshots = snapCount;

            // Load inventory object count from snapshot summaries
            try
            {
                var summaries = await _mediator.Send(new ListSnapshotsWithInventorySummaryQuery());
                objCount = summaries.Sum(s => s.TotalObjects);
            }
            catch { /* non-critical */ }
            TotalInventoryObjects = objCount;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void QuickNavigate(string section)
    {
        if (Enum.TryParse<NavSection>(section, out var nav))
            NavigateRequested?.Invoke(nav);
    }
}
