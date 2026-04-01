using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Environments;

public sealed record EnvironmentRelationDto(
    Guid Id,
    Guid CustomerId,
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    RelationType RelationType,
    RelationDirection Direction,
    string? Notes,
    DateTimeOffset CreatedAt);
