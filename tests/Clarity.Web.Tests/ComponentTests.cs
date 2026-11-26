using Bunit;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;

namespace Clarity.Web.Tests;

public class ComponentTests : BunitContext
{
    public ComponentTests()
    {
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Empty_state_preserves_accessible_action_and_label()
    {
        var component = Render<EmptyState>(parameters => parameters
            .Add(x => x.Title, "Aucun dossier").Add(x => x.Description, "Déclarez votre source.")
            .AddChildContent("<a href='/'>Créer un dossier</a>"));
        var section = component.Find("section");
        component.Find("h2").Id.ShouldBe(section.GetAttribute("aria-labelledby"));
        component.Find("a").TextContent.ShouldBe("Créer un dossier");
    }

    [Fact]
    public async Task Detail_panel_can_be_closed_with_escape()
    {
        var closed = false;
        var component = Render<DetailPanel>(parameters => parameters
            .Add(x => x.Title, "Dossier").Add(x => x.Close, () => closed = true));
        await component.Find("aside").KeyDownAsync(Key.Escape);
        closed.ShouldBeTrue();
        component.Find("aside").GetAttribute("aria-labelledby").ShouldBe(component.Find("h2").Id);
    }

    [Fact]
    public async Task Grid_forwards_paging_and_sorting_to_the_server_provider()
    {
        var requests = new List<PageRequest>();
        Task<PageResult<ClientWorkspace>> Load(PageRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new PageResult<ClientWorkspace>(
                [new ClientWorkspace { Name = "Synthetic" }], 41));
        }
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<PropertyColumn<ClientWorkspace, string>>(0);
            builder.AddAttribute(1, "Property", (System.Linq.Expressions.Expression<Func<ClientWorkspace, string>>)(item => item.Name));
            builder.AddAttribute(2, "Title", "Nom");
            builder.AddAttribute(3, "Sortable", true);
            builder.CloseComponent();
        };
        var component = Render<ClarityGrid<ClientWorkspace>>(parameters => parameters
            .Add(x => x.Load, Load).Add(x => x.Columns, columns).Add(x => x.Search, "alpha"));
        component.WaitForAssertion(() => requests.ShouldNotBeEmpty());
        requests[^1].Count.ShouldBe(20);
        requests[^1].Search.ShouldBe("alpha");
        component.WaitForAssertion(() => component.FindAll("fluent-button")
            .Single(button => button.TextContent.Contains("Suivant")).HasAttribute("disabled").ShouldBeFalse());
        await component.FindAll("fluent-button").Single(button => button.TextContent.Contains("Suivant")).ClickAsync(new());
        component.WaitForAssertion(() => requests[^1].Start.ShouldBe(20));
        await component.InvokeAsync(() => component.FindComponent<FluentDataGrid<ClientWorkspace>>().Instance
            .SortByColumnAsync("Nom", DataGridSortDirection.Descending));
        component.WaitForAssertion(() => requests[^1].Descending.ShouldBeTrue());
        requests[^1].Sort.ShouldBe("Name");
    }

    [Fact]
    public void Report_preview_shows_recognized_columns_rejected_rows_origin_and_declared_limits_in_french()
    {
        var tenant = Guid.NewGuid().ToString();
        var csv = string.Join('\n',
            DiscoveryEndpointTests.MailboxHeader + ",Extra Column",
            "2026-05-31,adele.vance@contoso.example,Adele Vance,False,,2021-03-04,2026-05-30,1520,2147483648,105226698752,106300440576,107374182400,42,10485760,32212254720,True,7,x",
            "2026-05-31,lee.gu@contoso.example,Lee Gu,False,,2020-02-02,2026-05-28,10,12 MB,105226698752,106300440576,107374182400,1,1024,32212254720,False,7,x",
            "2026-05-31,0123456789ABCDEF0123456789ABCDEF,FEDCBA9876543210FEDCBA9876543210,False,,2020-05-05,2026-05-27,5,1024,105226698752,106300440576,107374182400,0,0,32212254720,False,7,x");
        var profile = ReportProfiles.Default.Require("m365.mailboxUsageDetail", "1.0.0");
        var report = ReportAdapter.Convert(System.Text.Encoding.UTF8.GetBytes(csv), profile,
            new ReportContext(tenant, new("tenant", tenant), new string('a', 64)), ReportLimits.Default).Diagnostics;

        var text = System.Net.WebUtility.HtmlDecode(Render<ReportPreview>(parameters => parameters.Add(x => x.Report, report)).Markup);

        text.ShouldContain("Interprétation du rapport");
        text.ShouldContain("ne constitue pas une preuve de connexion Microsoft Graph");
        text.ShouldContain("Microsoft 365 — détail d'utilisation des boîtes aux lettres");
        text.ShouldContain("Rapport d'utilisation Microsoft 365 au format CSV, déposé par un consultant");
        text.ShouldContain("31/05/2026");
        text.ShouldContain("Lignes exploitables : 1");
        text.ShouldContain("Lignes rejetées : 1");
        text.ShouldContain("Lignes à identité masquée : 1");
        text.ShouldContain("<code>Storage Used (Byte)</code>");
        text.ShouldContain("Colonnes non reconnues (ignorées)");
        text.ShouldContain("Extra Column");
        text.ShouldContain("Ligne 3 · Storage Used (Byte) : taille invalide (nombre entier d'octets attendu)");
        text.ShouldContain("le masquage des rapports est actif");
        text.ShouldNotContain("0123456789ABCDEF0123456789ABCDEF");
    }

    [Fact]
    public void Report_preview_shows_both_identity_namespaces_for_mixed_onedrive_rows()
    {
        var tenant = Guid.NewGuid().ToString();
        var csv = string.Join('\n',
            "Report Refresh Date,Site Id,Site URL,Owner Display Name,Is Deleted,Last Activity Date,File Count,Active File Count,Storage Used (Byte),Storage Allocated (Byte),Owner Principal Name,Report Period",
            "2026-05-31,6f1c2a10-1111-4a8b-9c11-000000000001,https://contoso-my.sharepoint.com/personal/adele_vance_contoso_example,Adele Vance,False,2026-05-30,1200,35,5368709120,1099511627776,adele.vance@contoso.example,7",
            "2026-05-31,,https://contoso-my.sharepoint.com/personal/alex_wilber_contoso_example,Alex Wilber,False,,0,0,0,1099511627776,alex.wilber@contoso.example,7");
        var profile = ReportProfiles.Default.Require("m365.oneDriveUsageAccountDetail", "1.0.0");
        var report = ReportAdapter.Convert(System.Text.Encoding.UTF8.GetBytes(csv), profile,
            new ReportContext(tenant, new("tenant", tenant), new string('a', 64)), ReportLimits.Default).Diagnostics;

        var text = System.Net.WebUtility.HtmlDecode(Render<ReportPreview>(parameters => parameters.Add(x => x.Report, report)).Markup);

        text.ShouldContain("<code>Site Id → sharepoint.siteId</code>");
        text.ShouldContain("<code>Site URL → m365report.siteUrl</code>");
        text.ShouldContain("Lignes exploitables : 2");
        text.ShouldContain("Lignes rejetées : 0");
    }

    [Fact]
    public void Every_validated_section_and_published_script_producer_has_french_texts()
    {
        var text = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<UiStrings>>();
        var catalog = DiscoveryCatalog.Load();
        foreach (var section in catalog.Sections.Values.Where(x => x.ProducerIds.Any(id => catalog.Producers[id].Readiness == "SyntheticValidated")))
        {
            text[$"Section_{section.Id}"].ResourceNotFound.ShouldBeFalse(section.Id);
        }
        foreach (var producer in ScriptBundle.Registry.ProducerIds)
        {
            text[$"Producer_{producer}"].ResourceNotFound.ShouldBeFalse(producer);
            // The unsuffixed script texts are the AD texts; every other family overrides them with "<key>_<producerId>".
            foreach (var key in producer == "clarity.ad" ? [] : new[] { "LocalScriptConnectionHint", "CreateScriptRequest", "DownloadScriptBundle", "ScriptCommandHint" })
            {
                text[$"{key}_{producer}"].ResourceNotFound.ShouldBeFalse($"{key}_{producer}");
            }
        }
    }

    [Fact]
    public void Every_supported_report_profile_and_declared_limit_has_a_french_label()
    {
        var text = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<UiStrings>>();
        foreach (var profile in ReportProfiles.Default.Supported)
        {
            text[$"ReportProfile_{profile.Id}"].ResourceNotFound.ShouldBeFalse(profile.Id);
            text[$"ReportProfileHint_{profile.Id}"].ResourceNotFound.ShouldBeFalse(profile.Id);
            foreach (var limit in profile.Coverage.Limits)
            {
                text[$"ReportLimit_{limit.Code}"].ResourceNotFound.ShouldBeFalse(limit.Code);
            }
        }
    }
}
