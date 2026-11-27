using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Components;
using Clarity.Web.Operations;
using Clarity.Web.Security;
using Clarity.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddClarityTelemetry();
builder.Services.AddClarityReadiness(builder.Configuration);
builder.Services.AddFluentUIComponents();
builder.Services.AddLocalization();
builder.Services.AddSingleton<IFluentLocalizer, Clarity.Web.FluentUiLocalizer>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture("fr-FR").AddSupportedCultures("fr-FR").AddSupportedUICultures("fr-FR");
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceAuthorizationHandler>();
builder.Services.AddScoped<WorkspaceContext>();
builder.Services.AddScoped<IWorkspaceContext>(services => services.GetRequiredService<WorkspaceContext>());
builder.Services.AddScoped<WorkspaceAuthorization>();
builder.Services.AddScoped<FoundationService>();
builder.Services.AddDiscovery(builder.Configuration);
builder.Services.AddExceptionHandler<SafeExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddClaritySql(builder.Configuration.GetConnectionString("Clarity")
    ?? (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? "Server=localhost;Database=Clarity;Integrated Security=True;Encrypt=True;Connect Timeout=3"
        : throw new InvalidOperationException("ConnectionStrings:Clarity must be configured.")));

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.Use(Telemetry.ObserveRequestAsync);
app.UseExceptionHandler();
app.UseRequestLocalization();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapClarityHealth();
app.MapFoundationEndpoints();
app.MapDiscoveryEndpoints();
app.MapExportEndpoints();
app.MapGet("/authentication/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }));
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapPost("/development/persona", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        var persona = form["persona"].ToString();
        if (!DevelopmentAuthenticationHandler.Personas.ContainsKey(persona)) { return Results.BadRequest(); }
        context.Response.Cookies.Append("Clarity.Persona", persona,
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, IsEssential = true });
        return Results.Redirect("/");
    }).RequireAuthorization();
}
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
