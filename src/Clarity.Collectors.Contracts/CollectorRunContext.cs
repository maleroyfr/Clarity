using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Collectors.Contracts;

/// <summary>
/// Context passed into every collector run. Contains environment identity,
/// auth configuration, and scoped logger.
/// </summary>
public sealed record CollectorRunContext(
    Guid SnapshotId,
    Guid CollectorRunId,
    Guid EnvironmentId,
    WorkloadArea WorkloadArea,
    AuthConfiguration AuthConfig,
    CollectorOptions Options,
    ILogger Logger,
    CancellationToken CancellationToken);

public sealed record CollectorOptions(
    int MaxPageSize = 999,
    int MaxRetries = 3,
    TimeSpan RetryDelay = default,
    bool IncludeRawData = false,
    string? TenantDomain = null);
