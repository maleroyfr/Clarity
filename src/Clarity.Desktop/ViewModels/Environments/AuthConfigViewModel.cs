using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Commands;
using Clarity.Application.Environments.Queries;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Environments;

public sealed partial class WorkloadAuthItem : ObservableObject
{
    private readonly Func<WorkloadAuthItem, Task> _onSave;

    public WorkloadArea WorkloadArea { get; }

    public string WorkloadDisplayName => WorkloadArea switch
    {
        WorkloadArea.EntraId => "Entra ID",
        WorkloadArea.Intune => "Intune",
        WorkloadArea.ExchangeOnline => "Exchange Online",
        WorkloadArea.SharePointOnline => "SharePoint Online",
        WorkloadArea.Teams => "Teams",
        WorkloadArea.OnPremAD => "On-Premises AD",
        WorkloadArea.OnPremExchange => "On-Premises Exchange",
        _ => WorkloadArea.ToString()
    };

    public string WorkloadIcon => WorkloadArea switch
    {
        WorkloadArea.EntraId => "People",
        WorkloadArea.Intune => "CellPhone",
        WorkloadArea.ExchangeOnline => "Mail",
        WorkloadArea.SharePointOnline => "Globe",
        WorkloadArea.Teams => "Chat",
        WorkloadArea.OnPremAD => "Server",
        WorkloadArea.OnPremExchange => "MailAll",
        _ => "Settings"
    };

    [ObservableProperty]
    private int _selectedAuthTypeIndex; // 0=Certificate, 1=ClientSecret, 2=WindowsIntegrated

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _certificateThumbprint = string.Empty;

    [ObservableProperty]
    private string _secretReference = string.Empty;

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    public bool ShowCertificateField => SelectedAuthTypeIndex == 0;
    public bool ShowSecretField => SelectedAuthTypeIndex == 1;
    public bool ShowAppFields => SelectedAuthTypeIndex != 2;

    public string StatusText => IsConfigured ? "Configured" : "Not configured";
    public string StatusLabel => IsConfigured ? "Active" : "Pending";

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnSelectedAuthTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowCertificateField));
        OnPropertyChanged(nameof(ShowSecretField));
        OnPropertyChanged(nameof(ShowAppFields));
    }

    public WorkloadAuthItem(WorkloadArea workloadArea, Func<WorkloadAuthItem, Task> onSave)
    {
        WorkloadArea = workloadArea;
        _onSave = onSave;
    }

    [RelayCommand]
    public async Task Save() => await _onSave(this);

    public AuthType SelectedAuthType => SelectedAuthTypeIndex switch
    {
        0 => AuthType.Certificate,
        1 => AuthType.ClientSecret,
        2 => AuthType.WindowsIntegrated,
        _ => AuthType.Certificate
    };
}

public sealed partial class AuthConfigViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid _environmentId;

    [ObservableProperty]
    private string _title = "Authentication Configuration";

    [ObservableProperty]
    private ObservableCollection<WorkloadAuthItem> _workloads = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public AuthConfigViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public void Initialize(EnvironmentDetailDto detail)
    {
        _environmentId = detail.Id;
        Title = $"Auth Configuration — {detail.Name}";

        var enabledWorkloads = detail.WorkloadAreas
            .Select(w => Enum.Parse<WorkloadArea>(w, ignoreCase: true))
            .ToList();

        var items = new ObservableCollection<WorkloadAuthItem>();
        foreach (var area in enabledWorkloads)
        {
            var item = new WorkloadAuthItem(area, OnSaveWorkload);

            var existing = detail.AuthConfigurations
                .FirstOrDefault(a => a.WorkloadArea == area && a.IsActive);

            if (existing is not null)
            {
                item.SelectedAuthTypeIndex = existing.AuthType switch
                {
                    AuthType.Certificate => 0,
                    AuthType.ClientSecret => 1,
                    AuthType.WindowsIntegrated => 2,
                    _ => 0
                };
                item.ClientId = existing.ClientId ?? string.Empty;
                item.TenantId = existing.TenantId ?? string.Empty;
                item.CertificateThumbprint = existing.CertificateThumbprint ?? string.Empty;
                item.IsConfigured = true;
            }

            items.Add(item);
        }

        Workloads = items;
    }

    private async Task OnSaveWorkload(WorkloadAuthItem item)
    {
        item.IsSaving = true;
        item.ErrorMessage = null;
        item.SuccessMessage = null;

        try
        {
            await _mediator.Send(new SetAuthConfigurationCommand(
                _environmentId,
                item.WorkloadArea,
                item.SelectedAuthType,
                string.IsNullOrWhiteSpace(item.ClientId) ? null : item.ClientId.Trim(),
                string.IsNullOrWhiteSpace(item.TenantId) ? null : item.TenantId.Trim(),
                string.IsNullOrWhiteSpace(item.CertificateThumbprint) ? null : item.CertificateThumbprint.Trim(),
                string.IsNullOrWhiteSpace(item.SecretReference) ? null : item.SecretReference.Trim()));

            item.IsConfigured = true;
            item.SuccessMessage = "Configuration saved successfully.";
            item.NotifyStatusChanged();
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsSaving = false;
        }
    }
}
