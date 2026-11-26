using Bunit;
using Clarity.Core;
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
    }
}
