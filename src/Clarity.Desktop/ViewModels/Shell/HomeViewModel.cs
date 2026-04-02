using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Customers.Commands;
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
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    public string WelcomeMessage { get; } = "Welcome to Clarity";
    public string SubMessage { get; } = "Audit and Discovery Platform for Microsoft 365 and Active Directory";

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
            foreach (var c in customers)
            {
                var envs = await _mediator.Send(
                    new Clarity.Application.Environments.Queries.ListEnvironmentsByCustomerQuery(c.Id, false));
                envCount += envs.Count;
            }
            TotalEnvironments = envCount;
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
}

