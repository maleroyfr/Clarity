using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Environments.Commands;
using Clarity.Application.Environments.Queries;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Environments;

/// <summary>Represents a selectable authentication type in the UI.</summary>
public sealed class AuthTypeOption
{
    public AuthType Value { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public AuthTypeOption(AuthType value)
    {
        Value = value;
        DisplayName = AuthTypeHelper.GetAuthTypeDisplayName(value);
        Description = AuthTypeHelper.GetAuthTypeDescription(value);
    }

    public override string ToString() => DisplayName;
}

public sealed partial class WorkloadAuthItem : ObservableObject
{
    private readonly Func<WorkloadAuthItem, Task> _onSave;

    public WorkloadArea WorkloadArea { get; }
    public bool IsCloud { get; }
    public bool IsOnPrem { get; }

    public string WorkloadDisplayName => WorkloadArea switch
    {
        WorkloadArea.EntraId => "Entra ID",
        WorkloadArea.Intune => "Intune",
        WorkloadArea.ExchangeOnline => "Exchange Online",
        WorkloadArea.SharePointOnline => "SharePoint Online",
        WorkloadArea.Teams => "Teams",
        WorkloadArea.OnPremAD => "On-Premises Active Directory",
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

    /// <summary>Valid auth types for this workload (filtered by cloud vs on-prem).</summary>
    public ObservableCollection<AuthTypeOption> AvailableAuthTypes { get; }

    [ObservableProperty]
    private AuthTypeOption? _selectedAuthTypeOption;

    // ── Cloud-specific fields ────────────────────────────────────────
    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _certificateThumbprint = string.Empty;

    [ObservableProperty]
    private string _secretReference = string.Empty;

    // ── On-prem-specific fields ──────────────────────────────────────
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _domainOrServer = string.Empty;

    // ── State ────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    // ── Computed visibility ──────────────────────────────────────────
    public bool ShowCloudFields => IsCloud && SelectedAuthType is not null;
    public bool ShowCertificateField => IsCloud && SelectedAuthType == AuthType.Certificate;
    public bool ShowSecretField => IsCloud && SelectedAuthType == AuthType.ClientSecret;
    public bool ShowOnPremServiceAccountFields =>
        IsOnPrem && SelectedAuthType == AuthType.ServiceAccount;
    public bool ShowOnPremIntegratedHint =>
        IsOnPrem && SelectedAuthType == AuthType.WindowsIntegrated;

    public string StatusText => IsConfigured ? "Configured" : "Not configured";
    public string StatusBadge => IsConfigured ? "✓ Active" : "Pending";

    public AuthType? SelectedAuthType => SelectedAuthTypeOption?.Value;

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadge));
    }

    partial void OnSelectedAuthTypeOptionChanged(AuthTypeOption? value)
    {
        OnPropertyChanged(nameof(SelectedAuthType));
        OnPropertyChanged(nameof(ShowCloudFields));
        OnPropertyChanged(nameof(ShowCertificateField));
        OnPropertyChanged(nameof(ShowSecretField));
        OnPropertyChanged(nameof(ShowOnPremServiceAccountFields));
        OnPropertyChanged(nameof(ShowOnPremIntegratedHint));
    }

    public WorkloadAuthItem(WorkloadArea workloadArea, Func<WorkloadAuthItem, Task> onSave)
    {
        WorkloadArea = workloadArea;
        IsCloud = AuthTypeHelper.IsCloudWorkload(workloadArea);
        IsOnPrem = AuthTypeHelper.IsOnPremWorkload(workloadArea);
        _onSave = onSave;

        var validTypes = AuthTypeHelper.GetValidAuthTypes(workloadArea);
        AvailableAuthTypes = new ObservableCollection<AuthTypeOption>(
            validTypes.Select(t => new AuthTypeOption(t)));
        SelectedAuthTypeOption = AvailableAuthTypes.FirstOrDefault();
    }

    [RelayCommand]
    public async Task Save() => await _onSave(this);
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
                // Restore the saved auth type — only if it's valid for this workload
                var matchingOption = item.AvailableAuthTypes
                    .FirstOrDefault(o => o.Value == existing.AuthType);
                if (matchingOption is not null)
                    item.SelectedAuthTypeOption = matchingOption;

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
        if (item.SelectedAuthType is null) return;

        item.IsSaving = true;
        item.ErrorMessage = null;
        item.SuccessMessage = null;

        try
        {
            await _mediator.Send(new SetAuthConfigurationCommand(
                _environmentId,
                item.WorkloadArea,
                item.SelectedAuthType.Value,
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
