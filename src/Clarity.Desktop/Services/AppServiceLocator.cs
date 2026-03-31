using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Desktop.Services;

/// <summary>
/// Simple service locator for Avalonia — bridges DI container into ViewModels
/// that Avalonia instantiates via ViewLocator. Only used at the top-level shell;
/// all child ViewModels receive dependencies via constructor injection.
/// </summary>
public static class AppServiceLocator
{
    public static IServiceProvider ServiceProvider { get; set; } = default!;

    public static T Get<T>() where T : notnull =>
        ServiceProvider.GetRequiredService<T>();

    public static IServiceScope CreateScope() =>
        ServiceProvider.CreateScope();
}
