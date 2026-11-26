using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class PublicationService(
    IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, PackageValidator validator)
{
    public async Task PublishSectionAsync(Guid projectId, Guid runId, DiscoveryPackage package, string sectionId,
        CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        validator.Validate(package);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
        foreach (var record in package.Objects.Where(x => x.SectionId == sectionId))
        {
            var reference = new ExternalReference(record.Type, record.Namespace, record.ExternalId);
            var key = await FindKeyAsync(reference);
            var objectId = key?.ObjectId ?? Guid.NewGuid();
            if (key is null)
            {
                db.InventoryObjects.Add(new InventoryObject
                {
                    Id = objectId,
                    WorkspaceId = project.WorkspaceId,
                    EnvironmentId = run.EnvironmentId,
                    Type = record.Type
                });
                AddKey(reference, objectId);
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
        await AlternateKeyProjection.RebuildAsync(db, project.WorkspaceId, run.EnvironmentId, changedObjects, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return;

        async Task<ExternalKey?> FindKeyAsync(ExternalReference reference) =>
            db.ExternalKeys.Local.FirstOrDefault(x => x.IsPrimary && x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == run.EnvironmentId
                && x.Namespace == reference.Namespace && x.Type == reference.Type && x.ExternalId == reference.ExternalId)
            ?? await db.ExternalKeys.SingleOrDefaultAsync(x => x.IsPrimary && x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == run.EnvironmentId
                && x.Namespace == reference.Namespace && x.Type == reference.Type && x.ExternalId == reference.ExternalId, cancellationToken);

        void AddKey(ExternalReference reference, Guid objectId) => db.ExternalKeys.Add(new ExternalKey
        {
            WorkspaceId = project.WorkspaceId,
            EnvironmentId = run.EnvironmentId,
            ObjectId = objectId,
            Namespace = reference.Namespace,
            Type = reference.Type,
            ExternalId = reference.ExternalId
        });
    }

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
