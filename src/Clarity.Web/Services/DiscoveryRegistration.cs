using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection("Retention")).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RetentionOptions>>().Value);
        services.AddSingleton(DiscoveryCatalog.Load());
        services.AddSingleton(provider => ReportProfiles.Load(provider.GetRequiredService<DiscoveryCatalog>()));
        services.AddSingleton<PackageValidator>();
        services.AddSingleton(new PackageLimits());
        services.AddSingleton<ZipPackageReader>();
        services.AddSingleton<IArtifactStore, BlobArtifactStore>();
        services.AddSingleton<IExportStore, BlobExportStore>();
        services.AddSingleton<IExportText, ResourceExportText>();
        services.AddHttpClient("DiscoveryGraph", client => client.Timeout = TimeSpan.FromSeconds(90))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IGraphSessionFactory>(provider => new KeyVaultGraphSessionFactory(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("DiscoveryGraph"), options));
        services.AddSingleton<EntraCollector>();
        services.AddScoped<IProjectAccess, WebProjectAccess>();
        services.AddScoped<DiscoveryQueries>();
        services.AddScoped<ImportService>();
        services.AddScoped<PublicationService>();
        services.AddScoped<InventoryGraph>();
        services.AddScoped<InventoryMatching>();
        services.AddScoped<InventoryProof>();
        services.AddScoped<InventoryQueries>();
        services.AddScoped<InventorySheet>();
        services.AddScoped<MappingReview>();
        services.AddScoped<SsoQualificationService>();
        services.AddScoped<TeamsQualificationService>();
        services.AddScoped<InventoryOverview>();
        services.AddScoped<ScopeService>();
        services.AddScoped<DependencyService>();
        services.AddScoped<RunComparisonService>();
        services.AddScoped<TargetComparisonService>();
        services.AddScoped<DecisionRegisterService>();
        services.AddScoped<ScenarioOverviewService>();
        services.AddScoped<ScriptRequestService>();
        services.AddScoped<GraphDiscoveryService>();
        services.AddScoped<JobService>();
        services.AddScoped<ExportService>();
        services.AddScoped<ProjectDeletionService>();
        if (storage && !string.IsNullOrWhiteSpace(sql))
        {
            services.AddSingleton(new SqlJobQueue(sql, TimeProvider.System));
            services.AddSingleton<ICollectionJobHandler, ImportJobHandler>();
            services.AddSingleton<ICollectionJobHandler, GraphJobHandler>();
            services.AddSingleton<ICollectionJobHandler, ExportJobHandler>();
            services.AddSingleton<ICollectionJobHandler, DeletionJobHandler>();
            services.AddSingleton<CollectionJobProcessor>();
            services.AddHostedService<CollectionWorker>();
            services.AddSingleton(provider => new RetentionSweeper(sql, provider.GetRequiredService<IArtifactStore>(),
                provider.GetRequiredService<IExportStore>(), provider.GetRequiredService<RetentionOptions>(), TimeProvider.System,
                provider.GetRequiredService<ILogger<RetentionSweeper>>()));
            services.AddHostedService<RetentionWorker>();
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
        if (!ProjectLifecycle.Permits(project.Status, operation is WorkspaceOperation.Read or WorkspaceOperation.DeleteProject
            || await db.Workspaces.AnyAsync(x => x.Id == project.WorkspaceId && x.Status == WorkspaceStatus.Active, cancellationToken), operation))
        {
            throw new AccessDeniedException();
        }
        return project;
    }
}
