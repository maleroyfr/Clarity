using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Clarity.Web;

public sealed class FluentUiLocalizer(IStringLocalizer<UiStrings> text) : IFluentLocalizer
{
    public string this[string key, params object[] arguments]
    {
        get
        {
            var value = text[key, arguments];
            return value.ResourceNotFound ? IFluentLocalizer.GetDefault(key, arguments) : value.Value;
        }
    }
}
