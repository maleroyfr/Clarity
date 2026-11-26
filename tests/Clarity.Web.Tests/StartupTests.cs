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
        html.ShouldContain("<h1>Clients</h1>");
        html.ShouldContain("Aucun espace client");
        html.ShouldContain("SQL n");
        html.ShouldNotContain("Hello, world!");
    }
}
