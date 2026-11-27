using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class TargetComparisonTests
{
    private static readonly Guid SourceTenant = Guid.Parse("00000000-0000-0000-0000-000000000411");
    private static readonly Guid TargetTenant = Guid.Parse("00000000-0000-0000-0000-000000000412");
    private static readonly DateTimeOffset Date = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Alice = "a4000000-0000-0000-0000-000000000001", Bob = "a4000000-0000-0000-0000-000000000002";
    private const string Carol = "a4000000-0000-0000-0000-000000000003", NewAlice = "b4000000-0000-0000-0000-000000000001";
    private const string NewBob = "b4000000-0000-0000-0000-000000000002", Dave = "b4000000-0000-0000-0000-000000000004";

    [SqlFact]
    public async Task ExistingTargetsProposeReviewableAssociationsWhilePlannedOrEmptyTargetsAreNeverCompared()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        var service = Service(fixture);

        var ruleOperation = Guid.NewGuid();
        var computed = new TargetRuleDraft("user", TargetRuleKind.ComputedUpn, "userPrincipalName", "", "Target.Example", 1,
            "Synthetic target UPN built from the source local part");
        var rule = await service.AddRuleAsync(project, computed, ruleOperation);
        (await service.AddRuleAsync(project, computed, ruleOperation)).ShouldBe(rule);
        await Should.ThrowAsync<ConflictException>(() => service.AddRuleAsync(project, computed with { Priority = 2 }, ruleOperation));
        await Should.ThrowAsync<ArgumentException>(() => service.AddRuleAsync(project, computed with { TargetDomain = "not a domain" }, Guid.NewGuid()));
        (await service.RulesAsync(project, false)).Single().ShouldSatisfyAllConditions(r => r.TargetDomain.ShouldBe("target.example"),
            r => r.TargetField.ShouldBe("userPrincipalName"));

        var operation = Guid.NewGuid();
        var id = await service.CompareAsync(project, seed.Source, seed.Target, operation);
        (await service.CompareAsync(project, seed.Source, seed.Target, operation)).ShouldBe(id);
        await Should.ThrowAsync<ConflictException>(() => service.CompareAsync(project, seed.Source, seed.Planned, operation));
        await Should.ThrowAsync<ArgumentException>(() => service.CompareAsync(project, seed.Target, seed.Source, Guid.NewGuid()));

        var summary = (await service.ComparisonsAsync(project)).Single();
        (summary.Data, summary.Matched, summary.Ambiguous, summary.Absent, summary.Unknown, summary.NotCompared, summary.Collisions)
            .ShouldBe((TargetDataState.Available, 2, 0, 0, 1, 0, 2));
        summary.Coverage.Select(c => (c.Side, c.Type, c.Objects, c.Exhaustive, c.MissingFields.Count))
            .ShouldBe([(EnvironmentRole.Source, "user", 3, false, 0), (EnvironmentRole.Target, "user", 3, false, 0)]);
        var outcomes = (await service.ItemsAsync(project, id, false, null, null, new())).Items.ToDictionary(i => i.SourceId!.Value);
        outcomes[seed.Ids[Alice]].ShouldSatisfyAllConditions(i => i.Outcome.ShouldBe(TargetOutcome.Matched),
            i => i.TargetId.ShouldBe(seed.Ids[NewAlice]), i => i.Key.ShouldBe("alice@target.example"), i => i.RuleId.ShouldBe(rule));
        outcomes[seed.Ids[Carol]].ShouldSatisfyAllConditions(i => i.Outcome.ShouldBe(TargetOutcome.Unknown), i => i.Reason.ShouldBe("TargetNotExhaustive"));
        var collisions = (await service.ItemsAsync(project, id, true, null, null, new())).Items;
        collisions.Select(c => (c.Collision, c.Key, c.Reason)).ShouldBe(
            [(CollisionKind.Smtp, "carol@shared.example", "SmtpTaken"), (CollisionKind.Domain, "shared.example", "SharedDomain")], ignoreOrder: true);
        collisions.Single(c => c.Collision == CollisionKind.Smtp).TargetId.ShouldBe(seed.Ids[Dave]);
        (await service.ItemsAsync(project, id, true, null, CollisionKind.Domain, new())).Items.Single().Key.ShouldBe("shared.example");

        var planned = await service.CompareAsync(project, seed.Source, seed.Planned, Guid.NewGuid());
        var empty = await service.CompareAsync(project, seed.Source, seed.Empty, Guid.NewGuid());
        var summaries = (await service.ComparisonsAsync(project)).ToDictionary(s => s.Id);
        summaries[planned].ShouldSatisfyAllConditions(s => s.Data.ShouldBe(TargetDataState.Planned), s => s.NotCompared.ShouldBe(3),
            s => s.TargetState.ShouldBe(TargetState.Planned), s => s.Hypotheses.ShouldBe("Synthetic tenant to create"), s => s.Collisions.ShouldBe(0));
        summaries[empty].ShouldSatisfyAllConditions(s => s.Data.ShouldBe(TargetDataState.NoData), s => s.NotCompared.ShouldBe(3));
        (await service.ItemsAsync(project, planned, false, TargetOutcome.NotCompared, null, new())).Items
            .ShouldAllBe(i => i.Reason == "TargetPlanned");
        (await service.ItemsAsync(project, empty, false, null, null, new())).Items.ShouldAllBe(i => i.Reason == "TargetDataMissing");

        var review = new MappingReview(fixture.Factory, fixture.Access, fixture.Scope);
        var queue = (await review.PageAsync(project, MappingKind.SourceTarget, EnvironmentRole.Source, "Queue", new())).Items;
        queue.Select(r => (r.AdId, r.EntraId, r.State, r.Rule)).ShouldBe([
            (seed.Ids[Alice], seed.Ids[NewAlice], MappingState.Proposed, AssociationRule.ComputedTargetUpn),
            (seed.Ids[Bob], seed.Ids[NewBob], MappingState.Proposed, AssociationRule.ComputedTargetUpn)], ignoreOrder: true);
        (await review.PageAsync(project, EnvironmentRole.Source, "All", new())).TotalCount.ShouldBe(0);
        (await review.PageAsync(project, EnvironmentRole.Target, "All", new())).TotalCount.ShouldBe(0);
        var alice = queue.Single(r => r.AdId == seed.Ids[Alice]);
        await review.ApplyAsync(project, [new(alice.Id, alice.Revision)], true, 1, "Confirmed against synthetic target directory");

        await UploadAsync(fixture, seed.Store, seed.Source, SourcePackage());
        await service.CompareAsync(project, seed.Source, seed.Target, Guid.NewGuid());
        (await service.ComparisonsAsync(project)).Count.ShouldBe(3);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.MappingDecisions.SingleAsync(m => m.Id == alice.Id)).State.ShouldBe(MappingState.Validated);
            (await db.MappingDecisions.CountAsync(m => m.Kind == MappingKind.Hybrid)).ShouldBe(0);
        }
        (await new InventoryOverview(fixture.Factory, fixture.Access).ReadAsync(project, EnvironmentRole.Source)).ReviewCount.ShouldBe(0);

        await service.RetireRuleAsync(project, rule, "Synthetic switch to the shared address rule");
        (await service.RulesAsync(project, false)).ShouldBeEmpty();
        (await service.RulesAsync(project, true)).Single().RetirementReason.ShouldBe("Synthetic switch to the shared address rule");
        await service.AddRuleAsync(project, new("user", TargetRuleKind.SharedAttribute, "mail", "mail", null, 1, "Synthetic shared SMTP address"),
            Guid.NewGuid());
        var refreshed = await service.CompareAsync(project, seed.Source, seed.Target, Guid.NewGuid());
        (await service.ItemsAsync(project, refreshed, false, TargetOutcome.Matched, null, new())).Items
            .Select(i => (i.SourceId!.Value, i.TargetId!.Value)).ShouldBe([(seed.Ids[Alice], seed.Ids[NewAlice]), (seed.Ids[Carol], seed.Ids[Dave])],
                ignoreOrder: true);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var decisions = await db.MappingDecisions.AsNoTracking().Where(m => m.Kind == MappingKind.SourceTarget).ToDictionaryAsync(m => m.AdObjectId);
            decisions[seed.Ids[Alice]].ShouldSatisfyAllConditions(d => d.State.ShouldBe(MappingState.ToReview),
                d => d.Rule.ShouldBe(AssociationRule.MappingRuleAttribute), d => d.EntraEnvironmentId.ShouldBe(seed.Target));
            decisions[seed.Ids[Bob]].ShouldSatisfyAllConditions(d => d.State.ShouldBe(MappingState.ToReview),
                d => d.Rule.ShouldBe(AssociationRule.NoCurrentCandidate));
            decisions[seed.Ids[Carol]].State.ShouldBe(MappingState.Proposed);
            (await db.MappingRevisions.Where(r => r.DecisionId == alice.Id).OrderBy(r => r.Number).Select(r => r.Reason).ToListAsync())
                .ShouldBe(["CandidateObserved", "Confirmed against synthetic target directory", "AssociationEvidenceChanged"]);
            (await db.AuditEvents.CountAsync(a => a.Action == "TargetComparison.Computed")).ShouldBe(5);
            (await db.AuditEvents.CountAsync(a => a.Action == "TargetMapping.RuleRetired" && a.ResourceId == rule)).ShouldBe(1);
        }
        var proof = await new InventoryProof(fixture.Factory, fixture.Access).ReadAsync(project, new(ProofKind.Mapping, alice.Id));
        proof.Sources.ShouldNotBeEmpty();
    }

    [SqlFact]
    public async Task ComparisonsAreWorkspaceIsolatedAndReadersCannotDocumentOrCompare()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        var service = Service(fixture);
        var rule = await service.AddRuleAsync(project, new("user", TargetRuleKind.SharedAttribute, "mail", "mail", null, 1, "Synthetic"), Guid.NewGuid());
        var id = await service.CompareAsync(project, seed.Source, seed.Target, Guid.NewGuid());

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new TargetComparisonService(other.Factory, other.Access, other.Scope, DiscoveryCatalog.Load());
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.RulesAsync(project, true));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.ComparisonsAsync(project));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.ItemsAsync(other.Project.Id, id, false, null, null, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.RetireRuleAsync(other.Project.Id, rule, "Wrong dossier"));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.CompareAsync(project, seed.Source, seed.Target, Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.CompareAsync(other.Project.Id, seed.Source, seed.Target, Guid.NewGuid()));
        (await foreign.ComparisonsAsync(other.Project.Id)).ShouldBeEmpty();

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        (await service.ComparisonsAsync(project)).Single().Id.ShouldBe(id);
        (await service.ItemsAsync(project, id, false, null, null, new())).TotalCount.ShouldBe(3);
        await Should.ThrowAsync<AccessDeniedException>(() => service.CompareAsync(project, seed.Source, seed.Target, Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => service.AddRuleAsync(project,
            new("user", TargetRuleKind.SharedAttribute, "mail", "mail", null, 2, "Reader"), Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => service.RetireRuleAsync(project, rule, "Reader"));

        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.TargetMappingRules.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.TargetComparisons.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.TargetComparisonItems.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static TargetComparisonService Service(DiscoveryFixture fixture) =>
        new(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());

    private sealed record Seed(Guid Source, Guid Target, Guid Planned, Guid Empty, Dictionary<string, Guid> Ids, MemoryArtifactStore Store);

    /// <summary>
    /// A source tenant, an existing target with published data, a planned target and an existing target without data. The source and target
    /// share one accepted domain and the target's Dave already owns Carol's source address.
    /// </summary>
    private static async Task<Seed> SeedAsync(DiscoveryFixture fixture)
    {
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        }
        ProjectEnvironment Environment(Guid tenant, EnvironmentRole role, TargetState state = TargetState.Existing, string hypotheses = "") => new()
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = tenant.ToString(),
            Role = role,
            TargetState = state,
            Hypotheses = hypotheses
        };
        var source = Environment(SourceTenant, EnvironmentRole.Source);
        var target = Environment(TargetTenant, EnvironmentRole.Target);
        var planned = Environment(Guid.Parse("00000000-0000-0000-0000-000000000413"), EnvironmentRole.Target, TargetState.Planned,
            "Synthetic tenant to create");
        var empty = Environment(Guid.Parse("00000000-0000-0000-0000-000000000414"), EnvironmentRole.Target);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Environments.AddRange(source, target, planned, empty);
            await db.SaveChangesAsync();
        }
        var store = new MemoryArtifactStore();
        await UploadAsync(fixture, store, source.Id, SourcePackage());
        await UploadAsync(fixture, store, target.Id, TargetPackage());
        await using var check = await fixture.Factory.CreateDbContextAsync();
        var keys = await check.ExternalKeys.AsNoTracking().Where(k => k.IsPrimary && k.Type == "user").ToListAsync();
        return new(source.Id, target.Id, planned.Id, empty.Id,
            keys.ToDictionary(k => k.ExternalId, k => k.ObjectId, StringComparer.OrdinalIgnoreCase), store);
    }

    private static async Task UploadAsync(DiscoveryFixture fixture, MemoryArtifactStore store, Guid environment, DiscoveryPackage package)
    {
        DiscoveryCatalog.Load().ValidatePackage(package);
        new PackageValidator().Validate(package);
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(fixture.Project.Id, environment, new MemoryStream(await ZipImportTests.BytesAsync(package)), "package.zip");
        await imports.ConfirmAsync(fixture.Project.Id, run);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
    }

    private static DiscoveryPackage SourcePackage() => Package(SourceTenant,
        [User(Alice, "alice@source.example", "alice@source.example"), User(Bob, "bob@source.example", "bob@source.example"),
            User(Carol, "carol@source.example", "carol@shared.example")],
        ["source.example", "shared.example"]);

    private static DiscoveryPackage TargetPackage() => Package(TargetTenant,
        [User(NewAlice, "alice@target.example", "alice@source.example"), User(NewBob, "bob@target.example", "bob@target.example"),
            User(Dave, "dave@target.example", "carol@shared.example")],
        ["target.example", "shared.example"]);

    private static ObjectRecord User(string id, string upn, string mail) => new("entra.users", "user", "microsoft.graph", id, Date,
        new Dictionary<string, JsonElement>
        {
            ["displayName"] = JsonSerializer.SerializeToElement(upn[..upn.IndexOf('@', StringComparison.Ordinal)]),
            ["userPrincipalName"] = JsonSerializer.SerializeToElement(upn),
            ["mail"] = JsonSerializer.SerializeToElement(mail)
        });

    private static DiscoveryPackage Package(Guid tenant, ObjectRecord[] users, string[] domains)
    {
        var scope = new CollectionScope("tenant", tenant.ToString());
        var domainRecords = domains.Select(d => new ObjectRecord("entra.domains", "domain", "microsoft.graph", d, Date,
            new Dictionary<string, JsonElement> { ["isVerified"] = JsonSerializer.SerializeToElement(true) })).ToArray();
        return new(new PackageManifest("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant", tenant.ToString(), scope, Date, Date,
                new Dictionary<string, string>(), "Synthetic source-target comparison"),
            [.. users, .. domainRecords], [],
            [
                new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, scope, ["displayName", "userPrincipalName", "mail"], [],
                    users.Length, 0, [], []),
                new SectionResult("entra.domains", "entra.domains", CoverageStatus.Complete, scope, ["isVerified"], [], domainRecords.Length, 0, [], [])
            ]);
    }
}
