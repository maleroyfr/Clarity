using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Exports;

public sealed record ExportJobDto(
    Guid Id,
    JobStatus Status,
    string? OutputPath,
    long BytesWritten,
    string? ErrorMessage);
