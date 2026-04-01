using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Common.Exceptions;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Commands;
using Clarity.Domain.Environments;
using MediatR;

namespace Clarity.Desktop.ViewModels.Environments;

public sealed partial class EnvironmentFormViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid? _editingId;
    private Guid _customerId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private EnvironmentTypeOption _selectedTypeOption = EnvironmentTypeOption.All[0];

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _tenantDomain = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditMode;

    public string Title => IsEditMode ? "Edit Environment" : "New Environment";

    public IReadOnlyList<EnvironmentTypeOption> AvailableTypes => EnvironmentTypeOption.All;

    public event Action? SaveCompleted;
    public event Action? Cancelled;

    public EnvironmentFormViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public void Initialize(EnvironmentDto? existing, Guid customerId)
    {
        _customerId = customerId;
        if (existing is not null)
        {
            _editingId = existing.Id;
            Name = existing.Name;
            Description = existing.Description ?? string.Empty;
            SelectedTypeOption = EnvironmentTypeOption.All
                .FirstOrDefault(o => o.Value == existing.Type) ?? EnvironmentTypeOption.All[0];
            TenantId = existing.TenantId?.ToString() ?? string.Empty;
            TenantDomain = existing.TenantDomain ?? string.Empty;
            IsEditMode = true;
        }
        else
        {
            _editingId = null;
            Name = string.Empty;
            Description = string.Empty;
            SelectedTypeOption = EnvironmentTypeOption.All[0];
            TenantId = string.Empty;
            TenantDomain = string.Empty;
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
            ErrorMessage = "Environment name is required.";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            Guid? parsedTenantId = Guid.TryParse(TenantId, out var tid) ? tid : null;
            var domain = string.IsNullOrWhiteSpace(TenantDomain) ? null : TenantDomain.Trim();

            if (IsEditMode && _editingId.HasValue)
            {
                await _mediator.Send(new UpdateEnvironmentCommand(
                    _editingId.Value, Name.Trim(), Description.TrimOrNull(), domain));
            }
            else
            {
                await _mediator.Send(new CreateEnvironmentCommand(
                    _customerId, Name.Trim(), SelectedTypeOption.Value,
                    Description.TrimOrNull(), parsedTenantId, domain, []));
            }

            SaveCompleted?.Invoke();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = FormatValidationErrors(vex.Errors);
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

    private static string FormatValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var messages = errors
            .SelectMany(kvp => kvp.Value)
            .Where(m => !string.IsNullOrWhiteSpace(m));
        return string.Join("\n", messages);
    }
}

file static class EnvFormStringExtensions
{
    internal static string? TrimOrNull(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
