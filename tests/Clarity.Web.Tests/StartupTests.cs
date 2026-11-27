using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Clarity.Web.Tests;

public class StartupTests
{
    [Fact]
    public async Task Empty_application_can_render_the_home_page()
    {
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
        document.QuerySelector("main h1").ShouldNotBeNull().TextContent.ShouldBe("Clients");
        document.QuerySelector("body[use-reboot]").ShouldNotBeNull();
        html.ShouldContain("La liste des clients est indisponible");
        html.ShouldContain("ConnectionStrings:Clarity");
        html.ShouldNotContain("Aucun espace client");
        html.ShouldNotContain("Hello, world!");
    }
}
