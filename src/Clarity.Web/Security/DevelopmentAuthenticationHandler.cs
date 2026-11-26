using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Clarity.Web.Security;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public static readonly Guid SyntheticTenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid AlphaWorkspace = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid BetaWorkspace = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    public static readonly IReadOnlyDictionary<string, Guid> Personas = new Dictionary<string, Guid>
    {
        ["manager"] = Guid.Parse("10000000-0000-4000-8000-000000000001"),
        ["consultant"] = Guid.Parse("10000000-0000-4000-8000-000000000002"),
        ["reader"] = Guid.Parse("10000000-0000-4000-8000-000000000003"),
        ["platform-admin"] = Guid.Parse("10000000-0000-4000-8000-000000000004"),
        ["beta-manager"] = Guid.Parse("20000000-0000-4000-8000-000000000001")
    };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var persona = Request.Headers["X-Clarity-Persona"].FirstOrDefault()
            ?? Request.Cookies["Clarity.Persona"] ?? "manager";
        if (!Personas.TryGetValue(persona, out var objectId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown development persona."));
        }
        var identity = new ClaimsIdentity(
        [
            new Claim("tid", SyntheticTenant.ToString()),
            new Claim("oid", objectId.ToString()),
            new Claim(ClaimTypes.Name, persona)
        ], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
