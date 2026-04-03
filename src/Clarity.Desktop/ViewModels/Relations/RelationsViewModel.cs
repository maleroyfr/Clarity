using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Application.Customers.Queries;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Commands;
using Clarity.Application.Environments.Queries;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Relations;

/// <summary>Per-row ViewModel for a single environment relation.</summary>
public sealed partial class RelationListItemVm : ObservableObject
{
    private readonly Func<EnvironmentRelationDto, Task> _onDelete;
    private readonly EnvironmentRelationDto _dto;

    public Guid Id { get; }
    public string SourceEnvironmentName { get; }
    public string TargetEnvironmentName { get; }
    public RelationType RelationType { get; }
    public RelationDirection Direction { get; }
    public string? Notes { get; }
    public DateTimeOffset CreatedAt { get; }

    public string RelationTypeDisplay => RelationType switch
    {
        SharedContracts.Enums.RelationType.MergerAcquisition => "Merger / Acquisition",
        SharedContracts.Enums.RelationType.Migration => "Migration",
        SharedContracts.Enums.RelationType.Coexistence => "Coexistence",
        SharedContracts.Enums.RelationType.Divestiture => "Divestiture",
        SharedContracts.Enums.RelationType.Benchmark => "Benchmark",
        _ => "Other"
    };

    public string DirectionDisplay => Direction == RelationDirection.Bidirectional
        ? "⇄ Bidirectional"
        : "→ Unidirectional";

    public string DirectionArrow => Direction == RelationDirection.Bidirectional ? "⇄" : "→";
    public string CreatedAtDisplay => $"Created {CreatedAt:MMM d, yyyy}";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public RelationListItemVm(
        EnvironmentRelationDto dto,
        string sourceEnvName,
        string targetEnvName,
        Func<EnvironmentRelationDto, Task> onDelete)
    {
        _dto = dto;
        _onDelete = onDelete;
        Id = dto.Id;
        SourceEnvironmentName = sourceEnvName;
        TargetEnvironmentName = targetEnvName;
        RelationType = dto.RelationType;
        Direction = dto.Direction;
        Notes = dto.Notes;
        CreatedAt = dto.CreatedAt;
    }

    [RelayCommand]
    public async Task Delete() => await _onDelete(_dto);
}

/// <summary>Selectable customer item for the customer picker.</summary>
public sealed class RelationCustomerPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>Selectable environment item for the form pickers.</summary>
public sealed class EnvironmentPickerItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

public sealed partial class RelationsViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    // ── Customer picker ──────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<RelationCustomerPickerItem> _customers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private RelationCustomerPickerItem? _selectedCustomer;

    [ObservableProperty]
    private bool _isLoadingCustomers;

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    // ── Relations list ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasRelations))]
    private ObservableCollection<RelationListItemVm> _relations = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasRelations))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsEmpty => !IsLoading && HasSelectedCustomer && Relations.Count == 0;
    public bool HasRelations => !IsLoading && Relations.Count > 0;

    // ── Inline create form ───────────────────────────────────────────────
    [ObservableProperty]
    private bool _isFormVisible;

    [ObservableProperty]
    private ObservableCollection<EnvironmentPickerItem> _environments = [];

    [ObservableProperty]
    private EnvironmentPickerItem? _selectedSourceEnvironment;

    [ObservableProperty]
    private EnvironmentPickerItem? _selectedTargetEnvironment;

    [ObservableProperty]
    private RelationType _selectedRelationType;

    [ObservableProperty]
    private RelationDirection _selectedDirection;

    [ObservableProperty]
    private string? _newNotes;

    [ObservableProperty]
    private bool _isSaving;

    public IReadOnlyList<RelationType> RelationTypes { get; } =
        Enum.GetValues<RelationType>();

    public IReadOnlyList<RelationDirection> RelationDirections { get; } =
        Enum.GetValues<RelationDirection>();

    private Dictionary<Guid, string> _environmentNames = [];

    public RelationsViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Loads the customer list for the picker.</summary>
    public async Task LoadCustomersAsync()
    {
        IsLoadingCustomers = true;
        try
        {
            var dtos = await _mediator.Send(new ListCustomersQuery(IncludeArchived: false));
            Customers = new ObservableCollection<RelationCustomerPickerItem>(
                dtos.Select(c => new RelationCustomerPickerItem { Id = c.Id, Name = c.Name }));

            if (Customers.Count == 1)
                SelectedCustomer = Customers[0];
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load customers: {ex.Message}";
        }
        finally
        {
            IsLoadingCustomers = false;
        }
    }

    partial void OnSelectedCustomerChanged(RelationCustomerPickerItem? value)
    {
        if (value is not null)
            _ = LoadAsync();
        else
        {
            Relations = [];
            Environments = [];
            _environmentNames = [];
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasRelations));
        }
    }

    /// <summary>Loads relations and environments for the selected customer.</summary>
    public async Task LoadAsync()
    {
        if (SelectedCustomer is null) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var customerId = SelectedCustomer.Id;

            var relationsTask = _mediator.Send(new ListRelationsByCustomerQuery(customerId));
            var environmentsTask = _mediator.Send(new ListEnvironmentsByCustomerQuery(customerId, false));

            await Task.WhenAll(relationsTask, environmentsTask);

            var envDtos = environmentsTask.Result;
            _environmentNames = envDtos.ToDictionary(e => e.Id, e => e.Name);
            Environments = new ObservableCollection<EnvironmentPickerItem>(
                envDtos.Select(e => new EnvironmentPickerItem { Id = e.Id, Name = e.Name }));

            var relationDtos = relationsTask.Result;
            Relations = new ObservableCollection<RelationListItemVm>(
                relationDtos.Select(r => new RelationListItemVm(
                    r,
                    ResolveEnvironmentName(r.SourceEnvironmentId),
                    ResolveEnvironmentName(r.TargetEnvironmentId),
                    OnDeleteRelation)));

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasRelations));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load relations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string ResolveEnvironmentName(Guid environmentId) =>
        _environmentNames.TryGetValue(environmentId, out var name) ? name : "(unknown)";

    [RelayCommand(CanExecute = nameof(HasSelectedCustomer))]
    public void ShowForm()
    {
        SelectedSourceEnvironment = null;
        SelectedTargetEnvironment = null;
        SelectedRelationType = RelationType.Migration;
        SelectedDirection = RelationDirection.Unidirectional;
        NewNotes = null;
        IsFormVisible = true;
    }

    [RelayCommand]
    public void CancelForm()
    {
        IsFormVisible = false;
    }

    [RelayCommand]
    public async Task SaveRelation()
    {
        if (SelectedCustomer is null
            || SelectedSourceEnvironment is null
            || SelectedTargetEnvironment is null)
            return;

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new CreateRelationCommand(
                SelectedCustomer.Id,
                SelectedSourceEnvironment.Id,
                SelectedTargetEnvironment.Id,
                SelectedRelationType,
                SelectedDirection,
                string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim()));

            IsFormVisible = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create relation: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task OnDeleteRelation(EnvironmentRelationDto dto)
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new DeleteRelationCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete relation: {ex.Message}";
        }
    }
}
