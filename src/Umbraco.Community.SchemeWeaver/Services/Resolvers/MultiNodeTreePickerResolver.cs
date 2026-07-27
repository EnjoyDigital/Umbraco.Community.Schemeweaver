using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves Multi Node Tree Picker property values. Each picked node goes through the
/// shared <see cref="PickedContentResolver"/> ladder (drill-down config, then whole-item
/// nesting, then node name), matching <see cref="ContentPickerResolver"/> semantics
/// per item.
///
/// Output shape: a single picked node returns its value directly (parity with the
/// single content picker); multiple nodes return a homogenised list —
/// <see cref="SchemaPropertySetter"/> only binds <c>IEnumerable&lt;Thing&gt;</c> or
/// <c>IEnumerable&lt;string&gt;</c>, so mixed results prefer Things and drop strings
/// (deterministic, rather than dropping the whole list). Non-content values (the raw
/// Udi[] the converter returns without an Umbraco context, or anything else) resolve
/// to null.
/// </summary>
public class MultiNodeTreePickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases => ["Umbraco.MultiNodeTreePicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);

        var pickedItems = value switch
        {
            IPublishedContent single => [single],
            IEnumerable<IPublishedContent> many => many.Where(item => item is not null).ToList(),
            _ => new List<IPublishedContent>()
        };

        if (pickedItems.Count == 0)
            return null;

        var config = PickedContentResolver.ParseConfig(context.Mapping.ResolverConfig);

        var resolved = new List<object>();
        foreach (var item in pickedItems)
        {
            // One bad item must never break the page (or the rest of the list).
            try
            {
                var itemValue = PickedContentResolver.ResolveItem(item, context, config);
                if (itemValue is null)
                    continue;

                // Flatten per-item enumerables (e.g. a drilled multi-media property
                // yields IEnumerable<IImageObject>) so homogenisation sees leaf values.
                if (itemValue is System.Collections.IEnumerable inner and not string)
                {
                    foreach (var leaf in inner)
                    {
                        if (leaf is not null)
                            resolved.Add(leaf);
                    }
                }
                else
                {
                    resolved.Add(itemValue);
                }
            }
            catch
            {
                // Skip the item; degrade to whatever the rest of the list yields.
            }
        }

        if (resolved.Count == 0)
            return null;

        if (resolved.Count == 1)
            return resolved[0];

        // Homogenise: SchemaPropertySetter binds IEnumerable<Thing> / IEnumerable<string>
        // but a List<object> matches neither and would be dropped wholesale.
        var things = resolved.OfType<Thing>().ToList();
        if (things.Count > 0)
            return things;

        return resolved.Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList();
    }
}
