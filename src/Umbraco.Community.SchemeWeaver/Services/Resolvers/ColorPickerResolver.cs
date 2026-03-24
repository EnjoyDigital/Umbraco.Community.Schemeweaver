using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves colour picker property values to a hex colour string with # prefix for Schema.NET.
/// The Umbraco.ColorPicker editor returns a <see cref="ColorPickerValueConverter.PickedColor"/>
/// or a plain string depending on configuration.
/// </summary>
public class ColorPickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.ColorPicker", "Umbraco.ColorPicker.EyeDropper"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        if (value is ColorPickerValueConverter.PickedColor pickedColor)
        {
            return EnsureHashPrefix(pickedColor.Color);
        }

        var colorString = value.ToString();
        return string.IsNullOrEmpty(colorString) ? null : EnsureHashPrefix(colorString);
    }

    private static string EnsureHashPrefix(string color)
    {
        return color.StartsWith('#') ? color : $"#{color}";
    }
}
