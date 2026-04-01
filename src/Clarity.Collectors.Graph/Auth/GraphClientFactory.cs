using Azure.Core;
using Azure.Identity;
using Clarity.Domain.Environments;
using Microsoft.Graph;
using System.Security.Cryptography.X509Certificates;

namespace Clarity.Collectors.Graph.Auth;

public interface IGraphClientFactory
{
    GraphServiceClient Create(AuthConfiguration authConfig);
}

public sealed class GraphClientFactory : IGraphClientFactory
{
    public GraphServiceClient Create(AuthConfiguration authConfig)
    {
        TokenCredential credential = authConfig.AuthType switch
        {
            AuthType.Certificate    => BuildCertificateCredential(authConfig),
            AuthType.ClientSecret   => BuildClientSecretCredential(authConfig),
            _ => throw new NotSupportedException(
                $"Auth type '{authConfig.AuthType}' is not supported for Graph API.")
        };

        return new GraphServiceClient(credential);
    }

    private static ClientCertificateCredential BuildCertificateCredential(AuthConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.TenantId, nameof(config.TenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ClientId, nameof(config.ClientId));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.CertificateThumbprint, nameof(config.CertificateThumbprint));

        var cert = FindCertificateByThumbprint(config.CertificateThumbprint);
        return new ClientCertificateCredential(config.TenantId, config.ClientId, cert);
    }

    private static ClientSecretCredential BuildClientSecretCredential(AuthConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.TenantId, nameof(config.TenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ClientId, nameof(config.ClientId));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.SecretReference, nameof(config.SecretReference));

        // TODO: implement Windows Credential Manager lookup.
        // For now: read from an environment variable whose name matches the reference key.
        var secret = System.Environment.GetEnvironmentVariable(config.SecretReference)
            ?? throw new InvalidOperationException(
                $"Client secret '{config.SecretReference}' not found in environment variables.");

        return new ClientSecretCredential(config.TenantId, config.ClientId, secret);
    }

    private static X509Certificate2 FindCertificateByThumbprint(string thumbprint)
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (certs.Count > 0)
                return certs[0];
        }

        throw new InvalidOperationException(
            $"Certificate with thumbprint '{thumbprint}' not found in CurrentUser\\My or LocalMachine\\My.");
    }
}
