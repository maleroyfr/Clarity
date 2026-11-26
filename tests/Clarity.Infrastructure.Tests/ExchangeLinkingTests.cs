using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ExchangeLinkingTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly CollectionScope Scope = new("tenant", Tenant.ToString());
    private static readonly string[] MailboxFields = ["displayName", "recipientTypeDetails", "externalDirectoryObjectId", "userPrincipalName", "mail"];

    private static string Entra(int number) => $"33333333-0000-0000-0000-{number:D12}";
    private static string Exchange(int number) => $"22222222-0000-0000-0000-{number:D12}";

    [SqlFact]
    public async Task UserMailboxesLinkByEntraObjectIdThenExactUpnThenSmtpAndNeverGuessAmbiguousMatches()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        await ImportAsync(fixture, environment, store, EntraPackage(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            (Entra(1), "alice@contoso.example", "alice@contoso.example"),
            (Entra(102), "bob@contoso.example", null),
            (Entra(103), "carol.alias@contoso.example", "carol@contoso.example"),
            (Entra(104), "dave@contoso.example", null),
            (Entra(105), "dave@contoso.example", null),
            (Entra(106), "dave.other@contoso.example", "dave@contoso.example")));
        await ImportAsync(fixture, environment, store, ExchangePackage(new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero)));

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var users = await KeysAsync(db, "microsoft.graph", "user");
        var exchange = await KeysAsync(db, "exchange.guid");
        var links = await db.InventoryRelationships.AsNoTracking().Where(x => x.RelationType == "mailboxOf").ToListAsync();
        InventoryRelationship Link(int mailbox) => links.Single(x => x.FromObjectId == exchange[Exchange(mailbox)]);

        links.Count.ShouldBe(6);
        (Link(1).ToMethod, Link(1).ToObjectId).ShouldBe((EndpointResolution.Primary, users[Entra(1)]));
        (Link(2).ToMethod, Link(2).ToObjectId).ShouldBe((EndpointResolution.Alternate, users[Entra(102)]));
        (Link(3).ToMethod, Link(3).ToObjectId).ShouldBe((EndpointResolution.Alternate, users[Entra(103)]));
        (Link(4).ToMethod, Link(4).ToObjectId).ShouldBe((EndpointResolution.Ambiguous, null));
        (Link(5).ToMethod, Link(5).ToObjectId).ShouldBe((EndpointResolution.Pending, null));
        (Link(6).ToMethod, Link(6).ToObjectId).ShouldBe((EndpointResolution.Pending, null));
        links.ShouldAllBe(x => x.FromMethod == EndpointResolution.Primary && x.Origin == RelationshipOrigin.Observed);

        // Shared mailboxes stay independent objects; they are never merged into a user.
        var shared = await db.InventoryObjects.AsNoTracking().SingleAsync(x => x.Id == exchange[Exchange(10)]);
        shared.Type.ShouldBe("sharedMailbox");
        links.ShouldNotContain(x => x.FromObjectId == shared.Id);
        (await db.InventoryObjects.CountAsync(x => x.Type == "user")).ShouldBe(6);
        var delegation = await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "fullAccessTo");
        (delegation.FromObjectId, delegation.ToObjectId, delegation.Origin).ShouldBe((exchange[Exchange(1)], shared.Id, RelationshipOrigin.ObservedDelegation));

        // A later Entra arrival with the mailbox's own object ID takes precedence over the UPN that was used meanwhile.
        await ImportAsync(fixture, environment, store, EntraPackage(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero),
            (Entra(2), "bob.renamed@contoso.example", null)));
        await using var later = await fixture.Factory.CreateDbContextAsync();
        var arrived = (await KeysAsync(later, "microsoft.graph", "user"))[Entra(2)];
        var updated = await later.InventoryRelationships.AsNoTracking().Where(x => x.RelationType == "mailboxOf" && x.FromObjectId == exchange[Exchange(2)]).ToListAsync();
        var bob = updated.ShouldHaveSingleItem();
        (bob.ToMethod, bob.ToObjectId).ShouldBe((EndpointResolution.Primary, arrived));
        arrived.ShouldNotBe(users[Entra(102)]);
    }

    private static DiscoveryPackage EntraPackage(DateTimeOffset date, params (string Id, string Upn, string? Mail)[] users)
    {
        string[] fields = users.Any(x => x.Mail is not null) ? ["userPrincipalName", "mail"] : ["userPrincipalName"];
        return new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant",
                Tenant.ToString(), Scope, date, date, new Dictionary<string, string>(), "Synthetic Graph"),
            users.Select(user => new ObjectRecord("entra.users", "user", "microsoft.graph", user.Id, date, Attributes(
                ("userPrincipalName", user.Upn), ("mail", user.Mail)))).ToArray(), [],
            [new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, Scope, fields, [], users.Length, 0, [], [])]);
    }

    private static DiscoveryPackage ExchangePackage(DateTimeOffset date)
    {
        var catalog = DiscoveryCatalog.Load();
        ObjectRecord Mailbox(string type, int number, string alias, int? entra, bool upn = true) => new("exchange.mailboxes", type, "exchange.guid",
            Exchange(number), date, Attributes(("displayName", alias), ("recipientTypeDetails", type == "mailbox" ? "UserMailbox" : "SharedMailbox"),
                ("externalDirectoryObjectId", entra is null ? null : Entra(entra.Value)), ("userPrincipalName", upn ? $"{alias}@contoso.example" : null),
                ("mail", $"{alias}@contoso.example")));
        ExternalReference Ref(string ns, string id) => new("user", ns, id);
        RelationshipRecord Link(int number, int entra, string alias) => new("exchange.mailboxes", "mailboxOf", new("mailbox", "exchange.guid", Exchange(number)),
            Ref("microsoft.graph", Entra(entra)), date, new Dictionary<string, JsonElement>
            {
                ["fallbackTo"] = JsonSerializer.SerializeToElement(new[] { Ref("upn", $"{alias}@contoso.example"), Ref("smtp", $"{alias}@contoso.example") },
                    ContractJson.Options)
            });
        var mailboxes = new[]
        {
            Mailbox("mailbox", 1, "alice", 1), Mailbox("mailbox", 2, "bob", 2), Mailbox("mailbox", 3, "carol", 3),
            Mailbox("mailbox", 4, "dave", 4), Mailbox("mailbox", 5, "erin", 5), Mailbox("mailbox", 6, "frank", 6),
            Mailbox("sharedMailbox", 10, "support", 10)
        };
        // A fallback that widens the target type or is not a reference must never link anything.
        var forged = new RelationshipRecord("exchange.mailboxes", "mailboxOf", new("mailbox", "exchange.guid", Exchange(6)),
            Ref("microsoft.graph", Entra(6)), date, new Dictionary<string, JsonElement>
            {
                ["fallbackTo"] = JsonSerializer.SerializeToElement(new object[]
                {
                    new ExternalReference("directoryObject", "upn", "alice@contoso.example"), "alice@contoso.example", 7
                }, ContractJson.Options)
            });
        var permission = new RelationshipRecord("exchange.mailboxPermissions", "fullAccessTo", new("mailbox", "exchange.guid", Exchange(1)),
            new("sharedMailbox", "exchange.guid", Exchange(10)), date);
        return new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.exchange", "1.0.0", "M365Tenant",
                Tenant.ToString(), Scope, date, date, new Dictionary<string, string>(), "Exchange Online PowerShell"),
            mailboxes, [Link(1, 1, "alice"), Link(2, 2, "bob"), Link(3, 3, "carol"), Link(4, 4, "dave"), Link(5, 5, "erin"), forged, permission],
            [
                new SectionResult("exchange.mailboxes", "exchange.mailboxes", CoverageStatus.Complete, Scope, MailboxFields,
                    catalog.Sections["exchange.mailboxes"].Relations, mailboxes.Length, 6, [], []),
                new SectionResult("exchange.mailboxPermissions", "exchange.mailboxPermissions", CoverageStatus.Complete, Scope, [],
                    catalog.Sections["exchange.mailboxPermissions"].Relations, 0, 1, [], [])
            ]);
    }

    private static Dictionary<string, JsonElement> Attributes(params (string Name, string? Value)[] values) => values
        .Where(x => x.Value is not null).ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value));

    private static async Task ImportAsync(DiscoveryFixture fixture, ProjectEnvironment environment, IArtifactStore store, DiscoveryPackage package)
    {
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(fixture.Project.Id, environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(package)), "package.zip");
        await imports.ConfirmAsync(fixture.Project.Id, run);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionRuns.AsNoTracking().SingleAsync(x => x.Id == run)).State.ShouldBe(CollectionRunState.Completed);
    }

    private static async Task<Dictionary<string, Guid>> KeysAsync(ClarityDbContext db, string ns, string? type = null) =>
        await db.ExternalKeys.AsNoTracking().Where(x => x.IsPrimary && x.Namespace == ns && (type == null || x.Type == type))
            .ToDictionaryAsync(x => x.ExternalId, x => x.ObjectId);

    private static async Task<ProjectEnvironment> AddTenantAsync(DiscoveryFixture fixture)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = Tenant.ToString()
        };
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment;
    }
}
