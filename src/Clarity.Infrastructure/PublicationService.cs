using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class PublicationService(
    IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, PackageValidator validator, DiscoveryCatalog catalog)
{
    public async Task RebuildEnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await ImportService.EnvironmentAsync(db, project, environmentId, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        await LockEnvironmentAsync(db, project.WorkspaceId, environmentId, token);
        var ids = await db.InventoryObjects.Where(x => x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == environmentId)
            .Select(x => x.Id).ToListAsync(token);
        await AlternateKeyProjection.RebuildAsync(db, project.WorkspaceId, environmentId, ids, token);
        var affected = await CurrentProjection.RefreshAsync(db, catalog, project.WorkspaceId, environmentId, null, ids, token);
        await db.SaveChangesAsync(token);
        await DecisionReviewProjection.RefreshAsync(db, project.WorkspaceId, project.Id, affected, token);
        await RelationshipProjection.RefreshAsync(db, catalog, project.WorkspaceId, environmentId, token);
        await db.SaveChangesAsync(token);
        await InventoryMatching.RefreshAsync(db, project, token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task PublishSectionAsync(Guid projectId, Guid runId, DiscoveryPackage package, string sectionId,
        CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        validator.Validate(package);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InventoryMatching.LockProjectAsync(db, project, cancellationToken);
        var run = await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.Id == runId, cancellationToken) ?? throw new AccessDeniedException();
        if (run.ConfirmedAtUtc is null || run.ExternalRunId != package.Manifest.RunId
            || run.ManifestJson != JsonSerializer.Serialize(package.Manifest, ContractJson.Options))
        {
            throw new ConflictException();
        }
        await LockEnvironmentAsync(db, project.WorkspaceId, run.EnvironmentId, cancellationToken);
        await db.Entry(run).ReloadAsync(cancellationToken);
        if (run.State == CollectionRunState.Cancelled)
        {
            throw new OperationCanceledException("The collection was cancelled.");
        }
        var section = await db.SectionRuns.SingleAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.RunId == run.Id && x.SectionId == sectionId, cancellationToken);
        var result = package.Results.Single(x => x.SectionId == sectionId);
        if (section.ResultJson != JsonSerializer.Serialize(result, ContractJson.Options))
        {
            throw new ConflictException();
        }
        if (section.PublishedAtUtc is not null)
        {
            return;
        }
        var environment = await db.Environments.SingleAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.Id == run.EnvironmentId, cancellationToken);
        if (environment.Kind.ToString() != package.Manifest.EnvironmentKind
            || !string.Equals(environment.ExternalId, package.Manifest.EnvironmentExternalId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractException("EnvironmentMismatch", "manifest.json");
        }
        var changedObjects = new List<Guid>();
        using (Stage("Publication.Observations"))
        {
            await AddObservationsAsync(db, project, run, section, package, sectionId, changedObjects, cancellationToken);
        }
        using (Stage("Publication.AlternateKeys"))
        {
            await AlternateKeyProjection.RebuildAsync(db, project.WorkspaceId, run.EnvironmentId, changedObjects, cancellationToken);
        }
        IReadOnlyCollection<Guid> affected;
        using (Stage("Publication.CurrentValues"))
        {
            affected = await CurrentProjection.RefreshAsync(db, catalog, run.WorkspaceId, run.EnvironmentId, section, changedObjects, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        using (Stage("Publication.DecisionReview"))
        {
            await DecisionReviewProjection.RefreshAsync(db, run.WorkspaceId, project.Id, affected, cancellationToken);
        }
        using (Stage("Publication.Relationships"))
        {
            await RelationshipProjection.RefreshAsync(db, catalog, run.WorkspaceId, run.EnvironmentId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        using (Stage("Publication.Matching"))
        {
            await InventoryMatching.RefreshAsync(db, project, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        using (Stage("Publication.Commit"))
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task AddObservationsAsync(ClarityDbContext db, Project project, CollectionRun run, SectionRun section,
        DiscoveryPackage package, string sectionId, List<Guid> changedObjects, CancellationToken cancellationToken)
    {
        // Key columns use a binary collation, so ordinal tuple equality matches the unique primary-key index exactly.
        var keys = await db.ExternalKeys.AsNoTracking().Where(x => x.IsPrimary && x.WorkspaceId == project.WorkspaceId
                && x.EnvironmentId == run.EnvironmentId)
            .Select(x => new { x.Namespace, x.Type, x.ExternalId, x.ObjectId })
            .ToDictionaryAsync(x => (x.Namespace, x.Type, x.ExternalId), x => x.ObjectId, cancellationToken);
        foreach (var record in package.Objects.Where(x => x.SectionId == sectionId))
        {
            if (!keys.TryGetValue((record.Namespace, record.Type, record.ExternalId), out var objectId))
            {
                objectId = Guid.NewGuid();
                keys.Add((record.Namespace, record.Type, record.ExternalId), objectId);
                db.InventoryObjects.Add(new InventoryObject
                {
                    Id = objectId,
                    WorkspaceId = project.WorkspaceId,
                    EnvironmentId = run.EnvironmentId,
                    Type = record.Type
                });
                db.ExternalKeys.Add(new ExternalKey
                {
                    WorkspaceId = project.WorkspaceId,
                    EnvironmentId = run.EnvironmentId,
                    ObjectId = objectId,
                    Namespace = record.Namespace,
                    Type = record.Type,
                    ExternalId = record.ExternalId
                });
            }
            changedObjects.Add(objectId);
            db.ObjectObservations.Add(new ObjectObservation
            {
                WorkspaceId = project.WorkspaceId,
                ObjectId = objectId,
                SectionRunId = section.Id,
                ObservedAtUtc = record.ObservedAtUtc,
                AttributesJson = JsonSerializer.Serialize(record.Attributes, ContractJson.Options),
                AlternativeKeysJson = JsonSerializer.Serialize(record.AlternativeKeys, ContractJson.Options),
                UserPrincipalName = Text(record, "userPrincipalName", 500),
                Smtp = Text(record, "mail", 500),
                DisplayName = Text(record, "displayName", 500),
                Status = Text(record, "accountEnabled", 100) ?? Text(record, "enabled", 100)
            });
        }
        foreach (var record in package.Relationships.Where(x => x.SectionId == sectionId))
        {
            var from = JsonSerializer.Serialize(record.From, ContractJson.Options);
            var to = JsonSerializer.Serialize(record.To, ContractJson.Options);
            db.RelationshipObservations.Add(new RelationshipObservation
            {
                WorkspaceId = project.WorkspaceId,
                SectionRunId = section.Id,
                RelationType = record.RelationType,
                FromJson = from,
                ToJson = to,
                PropertiesJson = JsonSerializer.Serialize(record.Properties ?? new Dictionary<string, JsonElement>(), ContractJson.Options),
                IdentityHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new[] { record.RelationType, from, to })))),
                ObservedAtUtc = record.ObservedAtUtc
            });
        }
        section.PublishedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Publication stages as activities, so operators and the volume harness can see where a publication spends its time.</summary>
    public static readonly ActivitySource Activities = new("Clarity.Publication");

    private static Activity? Stage(string name) => Activities.StartActivity(name);

    internal static Task LockEnvironmentAsync(ClarityDbContext db, Guid workspaceId, Guid environmentId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {$"clarity:{workspaceId:N}:{environmentId:N}"},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Collection publication lock unavailable.', 1;
            """, cancellationToken);

    private static string? Text(ObjectRecord record, string name, int maxLength)
    {
        if (!record.Attributes.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (text?.Length > maxLength)
        {
            throw new ContractException("IndexedAttributeLength", "objects.jsonl");
        }
        return text;
    }
}
