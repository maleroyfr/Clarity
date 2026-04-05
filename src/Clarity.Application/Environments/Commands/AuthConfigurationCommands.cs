using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using DomainEnv = Clarity.Domain.Environments.Environment;

namespace Clarity.Application.Environments.Commands;

public sealed record SetAuthConfigurationCommand(
    Guid EnvironmentId,
    WorkloadArea WorkloadArea,
    AuthType AuthType,
    string? ClientId,
    string? TenantId,
    string? CertificateThumbprint,
    string? SecretReference) : ICommand;

public sealed class SetAuthConfigurationHandler(IEnvironmentRepository repo)
    : ICommandHandler<SetAuthConfigurationCommand>
{
    public async Task Handle(SetAuthConfigurationCommand cmd, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(cmd.EnvironmentId, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), cmd.EnvironmentId);

        var config = cmd.AuthType switch
        {
            AuthType.Certificate => AuthConfiguration.CreateCertificate(
                cmd.EnvironmentId, cmd.WorkloadArea, cmd.ClientId!, cmd.TenantId!, cmd.CertificateThumbprint!),
            AuthType.ClientSecret => AuthConfiguration.CreateClientSecret(
                cmd.EnvironmentId, cmd.WorkloadArea, cmd.ClientId!, cmd.TenantId!, cmd.SecretReference!),
            AuthType.WindowsIntegrated => AuthConfiguration.CreateWindowsIntegrated(
                cmd.EnvironmentId, cmd.WorkloadArea),
            _ => throw new ArgumentOutOfRangeException(nameof(cmd.AuthType))
        };

        env.SetAuthConfiguration(config);
        await repo.UpdateAsync(env, ct);
        await repo.SaveChangesAsync(ct);
    }
}
