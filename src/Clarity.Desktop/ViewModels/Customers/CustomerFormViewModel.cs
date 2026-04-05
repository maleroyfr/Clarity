using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Commands;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using MediatR;

namespace Clarity.Desktop.ViewModels.Customers;

public sealed partial class CustomerFormViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditMode;

    public string Title => IsEditMode ? "Edit Customer" : "New Customer";

    public event Action? SaveCompleted;
    public event Action? Cancelled;

    public CustomerFormViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public void Initialize(CustomerDto? existing)
    {
        if (existing is not null)
        {
            _editingId = existing.Id;
            Name = existing.Name;
            Description = existing.Description ?? string.Empty;
            IsEditMode = true;
        }
        else
        {
            _editingId = null;
            Name = string.Empty;
            Description = string.Empty;
            IsEditMode = false;
        }
        ErrorMessage = null;
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            if (IsEditMode && _editingId.HasValue)
                await _mediator.Send(new UpdateCustomerCommand(_editingId.Value, Name.Trim(), Description.TrimOrNull()));
            else
                await _mediator.Send(new CreateCustomerCommand(Name.Trim(), Description.TrimOrNull()));

            var action = IsEditMode ? "updated" : "created";
            AppServiceLocator.Get<AppShellViewModel>().ShowToast("Customer Saved", $"\"{Name.Trim()}\" has been {action}.", NotificationType.Success);
            SaveCompleted?.Invoke();
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
}

internal static class StringExtensions
{
    internal static string? TrimOrNull(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
