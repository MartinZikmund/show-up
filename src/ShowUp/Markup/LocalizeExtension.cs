using ShowUp.Services.Localization;
using Microsoft.UI.Xaml.Markup;

namespace ShowUp.Markup;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        return Localizer.Instance.GetString(Key);
    }
}
