using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Clarity.Desktop.ViewModels;

namespace Clarity.Desktop;

/// <summary>
/// Resolves Views from ViewModels by convention:
///   Clarity.Desktop.ViewModels.{Area}.{Name}ViewModel
///   → Clarity.Desktop.Views.{Area}.{Name}View
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var vmName = param.GetType().FullName!;

        // Convert ViewModels namespace to Views and strip "ViewModel" suffix
        var viewName = vmName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(viewName);
        if (type != null)
            return (Control)Activator.CreateInstance(type)!;

        return new TextBlock { Text = $"View not found: {viewName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
