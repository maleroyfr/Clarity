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
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<h1>");
    }
}
