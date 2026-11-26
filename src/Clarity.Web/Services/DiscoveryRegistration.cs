using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Web.Services;

public sealed record DiscoveryAvailability(bool Storage, bool Graph);

public static class DiscoveryRegistration
{
    public static void AddDiscovery(this IServiceCollection services, IConfiguration configuration)
    {
        var sql = configuration.GetConnectionString("Clarity");
        var storage = !string.IsNullOrWhiteSpace(configuration["Storage:BlobServiceUri"]);
        var options = new GraphCollectorOptions(configuration["GraphCollector:ClientId"], configuration["GraphCollector:KeyVaultUri"],
            configuration["GraphCollector:CertificateName"], configuration["GraphCollector:ManagedIdentityClientId"]);
        services.AddSingleton(new DiscoveryAvailability(storage && !string.IsNullOrWhiteSpace(sql), storage
            && Guid.TryParse(options.ClientId, out _) && !string.IsNullOrWhiteSpace(options.KeyVaultUri)
            && !string.IsNullOrWhiteSpace(options.CertificateName)));
        services.AddSingleton(options);
        services.AddSingleton(DiscoveryCatalog.Load());
        services.AddSingleton<PackageValidator>();
        services.AddSingleton(new PackageLimits());
        services.AddSingleton<ZipPackageReader>();
        services.AddSingleton<IArtifactStore, BlobArtifactStore>();
        services.AddHttpClient("DiscoveryGraph", client => client.Timeout = TimeSpan.FromSeconds(90))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IGraphSessionFactory>(provider => new KeyVaultGraphSessionFactory(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("DiscoveryGraph"), options));
        services.AddSingleton<EntraCollector>();
        services.AddScoped<IProjectAccess, WebProjectAccess>();
        services.AddScoped<DiscoveryQueries>();
        services.AddScoped<ImportService>();
        services.AddScoped<PublicationService>();
        services.AddScoped<ScriptRequestService>();
        services.AddScoped<GraphDiscoveryService>();
        services.AddScoped<JobService>();
        if (storage && !string.IsNullOrWhiteSpace(sql))
        {
            services.AddSingleton(new SqlJobQueue(sql, TimeProvider.System));
            services.AddSingleton<ICollectionJobHandler, ImportJobHandler>();
            services.AddSingleton<ICollectionJobHandler, GraphJobHandler>();
            services.AddSingleton<CollectionJobProcessor>();
            services.AddHostedService<CollectionWorker>();
        }
    }
}

public sealed class WebProjectAccess(IDbContextFactory<ClarityDbContext> factory, WorkspaceAuthorization authorization) : IProjectAccess
{
    public async Task<Project> RequireAsync(Guid projectId, WorkspaceOperation operation, CancellationToken cancellationToken = default)
    {
        await authorization.RefreshAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken)
            ?? throw new AccessDeniedException();
        await authorization.DemandAsync(project.WorkspaceId, operation);
        if (operation != WorkspaceOperation.Read && (project.Status != ProjectStatus.Active
            || !await db.Workspaces.AnyAsync(x => x.Id == project.WorkspaceId && x.Status == WorkspaceStatus.Active, cancellationToken)))
        {
            throw new AccessDeniedException();
        }
        return project;
    }
}
