using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Shared utility for setting Schema.NET property values with type conversion.
/// Handles implicit operators, OneOrMany&lt;T&gt;, Values&lt;T&gt;, and collection types.
/// </summary>
public static class SchemaPropertySetter
{
    /// <summary>
    /// Sets a property value on a Schema.NET Thing instance.
    /// Accepts string, Uri, Thing, or IEnumerable&lt;Thing&gt; values.
    /// </summary>
    public static void SetPropertyValue(Thing instance, string propertyName, object value, ILogger? logger = null)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
        {
            logger?.LogWarning(
                "Schema property '{PropertyName}' not found or not writable on {SchemaType}",
                propertyName, instance.GetType().Name);
            return;
        }

        var targetType = property.PropertyType;

        // Try each conversion strategy in declared order; the first that handles the value wins.
        // The order is load-bearing (identity first, image→Uri range guard before any coercion,
        // whole-collection before first-string fallback) — see the SetStrategies table.
        foreach (var strategy in SetStrategies)
        {
            if (strategy(property, instance, propertyName, targetType, value, logger))
                return;
        }

        // Nothing matched: the value's type can't be converted to the target
        // Schema.NET property's type (e.g. a Thing assigned to a scalar-only
        // property, or an object outside the property's range). Schema.NET would
        // silently drop it; log so the server isn't silent. The structural
        // SchemaRangeValidator surfaces the same problem to editors at save/preview.
        logger?.LogWarning(
            "Could not set Schema property '{PropertyName}' on {SchemaType}: value of type {ValueType} " +
            "is not convertible to the property's type {TargetType} and was dropped",
            propertyName, instance.GetType().Name, value.GetType().Name, targetType.Name);
    }

    /// <summary>
    /// One conversion strategy in the <see cref="SetPropertyValue"/> chain. Returns <c>true</c>
    /// when it has set the property (the chain then stops), <c>false</c> to defer to the next
    /// strategy. Strategies never throw on a bad match — they simply return <c>false</c>.
    /// </summary>
    private delegate bool SetStrategy(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger);

    /// <summary>
    /// The ordered conversion chain for <see cref="SetPropertyValue"/>. ORDER IS BEHAVIOURAL:
    /// the fast identity path runs first; the ImageObject→Uri range guard runs before any
    /// coercion; the whole-string-collection set runs before the first-string fallback.
    /// </summary>
    private static readonly SetStrategy[] SetStrategies =
    [
        TrySetAssignableIdentity,   // 1. value already the correct type — set directly
        TryDowngradeImageToUriLeaf, // 2. image→Uri range guard (REV-1)
        TrySetImplicit,             // 3. implicit conversion operator
        TrySetThingEnumerable,      // 4. IEnumerable<Thing> collection
        TrySetStringEnumerable,     // 5. IEnumerable<string> collection (+ first-string fallback)
        TrySetOneOrMany,            // 6. OneOrMany<T>
        TrySetValues,               // 7. Values<T1, T2, ...>
        TrySetStringAssignment,     // 8. plain string assignment
        TryWrapScalarThing,         // 9. scalar string → concrete Thing auto-wrap
    ];

    // If the value is already the correct type, set directly.
    private static bool TrySetAssignableIdentity(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
    {
        if (!targetType.IsInstanceOfType(value))
            return false;

        property.SetValue(instance, value);
        return true;
    }

    // Media values now arrive as Schema.NET ImageObject(s). When the target accepts
    // IImageObject (e.g. Article.Image, Organization.Logo) we fall through to the normal
    // OneOrMany/Values/collection handling below, which sets the ImageObject(s) directly.
    // But some targets accept only a Uri leaf (e.g. Thing.Url, contentUrl, sameAs) and NOT
    // IImageObject — an ImageObject would otherwise be dropped. Downgrade it to its URL.
    private static bool TryDowngradeImageToUriLeaf(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
    {
        if (IsImageObjectValue(value)
            && !TargetAcceptsLeaf(targetType, typeof(IImageObject))
            && TargetAcceptsLeaf(targetType, typeof(Uri)))
        {
            var downgraded = DowngradeImageToUri(value);
            if (downgraded is not null)
            {
                SetPropertyValue(instance, propertyName, downgraded, logger);
                return true;
            }
        }

        return false;
    }

    // Try to find an implicit conversion operator that accepts our value type.
    private static bool TrySetImplicit(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
        => TrySetViaImplicit(property, instance, targetType, value);

    // Handle IEnumerable<Thing> for collection properties (e.g., block content results).
    private static bool TrySetThingEnumerable(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
        => value is IEnumerable<Thing> things
           && TrySetCollectionValue(property, instance, targetType, things);

    // Handle IEnumerable<string> for string array properties (e.g., recipeIngredient).
    private static bool TrySetStringEnumerable(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
    {
        if (value is not IEnumerable<string> strings)
            return false;

        if (TrySetStringCollectionValue(property, instance, targetType, strings))
            return true;

        // The collection could not be set as a whole (e.g. a bare Values<...> target).
        // Fall back to the FIRST string so a multi-value resolver result (e.g. a
        // MultiUrlPicker with several links) never regresses a target that previously
        // accepted a single string value.
        var firstString = strings.FirstOrDefault(s => !string.IsNullOrEmpty(s));
        if (firstString is not null)
        {
            SetPropertyValue(instance, propertyName, firstString, logger);
            return true;
        }

        return false;
    }

    // Handle OneOrMany<T> types from Schema.NET by building from inside out.
    private static bool TrySetOneOrMany(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
        => targetType is { IsGenericType: true }
           && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany")
           && TrySetOneOrManyValue(property, instance, targetType, value);

    // Handle Values<T1, T2, ...> types directly (e.g., Image is Values<IImageObject, Uri>).
    private static bool TrySetValues(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
        => targetType is { IsGenericType: true }
           && targetType.GetGenericTypeDefinition().Name.StartsWith("Values")
           && TrySetValuesValue(property, instance, targetType, value);

    // Simple string assignment.
    private static bool TrySetStringAssignment(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
    {
        if (targetType != typeof(string) || value is not string strVal)
            return false;

        property.SetValue(instance, strVal);
        return true;
    }

    // Auto-wrap scalar strings into a concrete Thing for object-typed properties.
    // e.g. `Brand` mapped from a Textbox → { "@type": "Brand", "name": "AudioTech" }.
    // Users very commonly map Schema.org object properties (Brand, Author, Publisher)
    // from a plain string field; without this fallback Schema.NET silently drops the
    // value because no implicit conversion from string to IBrand/IPerson exists.
    private static bool TryWrapScalarThing(
        PropertyInfo property, Thing instance, string propertyName, Type targetType, object value, ILogger? logger)
    {
        if (value is not string scalarString || string.IsNullOrWhiteSpace(scalarString))
            return false;

        var wrapped = TryWrapScalarAsThing(targetType, propertyName, scalarString);
        if (wrapped is null)
            return false;

        // Re-enter the main setter with the wrapped Thing — it will match the
        // normal Thing-handling paths (implicit conversion / OneOrMany / Values).
        SetPropertyValue(instance, propertyName, wrapped, logger);
        return true;
    }

    /// <summary>
    /// True when <paramref name="thing"/> has at least one resolved Schema.org value property.
    /// Only properties whose type implements <see cref="IValues"/> (every <c>OneOrMany</c>/
    /// <c>Values</c> wrapper) with <c>Count &gt; 0</c> count — which cleanly excludes the
    /// <c>@type</c> (string), <c>@id</c> (Uri) and <c>@context</c> identity members and mirrors
    /// Schema.NET's own serializer, so "has a resolved property" ⇔ "will emit a property".
    /// Shared by the block-content empty-Thing drop (P2.1) and the complexType empty-shell guard.
    /// </summary>
    internal static bool HasResolvedProperty(Thing thing)
        => thing.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetIndexParameters().Length == 0
                      && p.GetValue(thing) is IValues { Count: > 0 });

    /// <summary>
    /// Whether the named schema property on <paramref name="instance"/> can carry a resolved
    /// media value: its (possibly generic) type accepts an <see cref="IImageObject"/> leaf
    /// directly, or a <see cref="Uri"/> leaf (in which case <see cref="SetPropertyValue"/>
    /// downgrades the image to its URL). A missing or read-only property accepts nothing.
    /// Render-time counterpart of the validator's AcceptsMedia check — when this returns
    /// false the setter would silently drop the ImageObject, which is the only case where
    /// the complexType adoption repair in <c>JsonLdGenerator</c> may fire.
    /// </summary>
    internal static bool PropertyAcceptsImageValue(Thing instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property is { CanWrite: true }
               && (TargetAcceptsLeaf(property.PropertyType, typeof(IImageObject))
                   || TargetAcceptsLeaf(property.PropertyType, typeof(Uri)));
    }

    /// <summary>
    /// Walks a generic property type looking for Schema.NET interface type arguments
    /// (e.g., IBrand, IPerson, IOrganization) so we can auto-construct a concrete Thing
    /// for scalar-to-object auto-wrapping.
    /// </summary>
    private static List<Type> CollectCandidateThingInterfaces(Type targetType)
    {
        var results = new List<Type>();
        var seen = new HashSet<Type>();

        void Walk(Type t)
        {
            if (!seen.Add(t))
                return;

            if (t.IsInterface && typeof(IThing).IsAssignableFrom(t))
            {
                results.Add(t);
                return;
            }

            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    Walk(arg);
            }
        }

        Walk(targetType);
        return results;
    }

    /// <summary>
    /// Whether a resolved value is a Schema.NET image — a single <see cref="IImageObject"/>,
    /// or a non-empty enumerable whose every element is an <see cref="IImageObject"/>. Used to
    /// decide whether the value needs image-aware handling (range-aware set or Uri downgrade).
    /// </summary>
    private static bool IsImageObjectValue(object? v)
    {
        if (v is IImageObject)
            return true;

        if (v is IEnumerable<IImageObject>)
            return true;

        // Non-generic enumerables (e.g. a List<object> of ImageObjects): every element must be
        // an IImageObject and there must be at least one. Exclude string (it's IEnumerable<char>).
        if (v is IEnumerable and not string)
        {
            var items = ((IEnumerable)v).Cast<object?>().ToList();
            return items.Count > 0 && items.All(x => x is IImageObject);
        }

        return false;
    }

    /// <summary>
    /// Walks a (possibly generic) target property type collecting its non-generic leaf type
    /// arguments — mirroring <see cref="CollectCandidateThingInterfaces"/>'s generic-walking style —
    /// and reports whether any leaf is equal to, or assignable from, <paramref name="leafType"/>.
    /// <see cref="Nullable{T}"/> leaves are unwrapped. E.g. <c>OneOrMany&lt;Values&lt;IImageObject, Uri&gt;&gt;</c>
    /// accepts both <c>IImageObject</c> and <c>Uri</c>; <c>OneOrMany&lt;Uri&gt;</c> accepts only <c>Uri</c>.
    /// </summary>
    private static bool TargetAcceptsLeaf(Type targetType, Type leafType)
    {
        var seen = new HashSet<Type>();
        var found = false;

        void Walk(Type t)
        {
            if (found || !seen.Add(t))
                return;

            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    Walk(arg);
                return;
            }

            var leaf = Nullable.GetUnderlyingType(t) ?? t;
            if (leaf == leafType || leaf.IsAssignableFrom(leafType))
                found = true;
        }

        Walk(targetType);
        return found;
    }

    /// <summary>
    /// Reduces an image value to a plain <see cref="Uri"/> (single) or <c>List&lt;Uri&gt;</c> (multi)
    /// for targets that accept only a Uri leaf. Reads each image's <c>Url</c> — a Schema.NET
    /// <c>OneOrMany&lt;Uri&gt;</c> — and takes its first entry, skipping images without a URL.
    /// Returns null when nothing usable remains.
    /// </summary>
    private static object? DowngradeImageToUri(object value)
    {
        if (value is IImageObject singleImage)
            return singleImage.Url.FirstOrDefault();

        IEnumerable<IImageObject> images = value switch
        {
            IEnumerable<IImageObject> typed => typed,
            IEnumerable and not string => ((IEnumerable)value).Cast<object?>().OfType<IImageObject>(),
            _ => []
        };

        var uris = images
            .Select(img => img.Url.FirstOrDefault())
            .Where(uri => uri is not null)
            .Cast<Uri>()
            .ToList();

        return uris.Count > 0 ? uris : null;
    }

    /// <summary>
    /// Builds a concrete <see cref="Thing"/> instance from a scalar string for an
    /// object-typed Schema.org property. Uses the property name as a hint to pick
    /// between multiple candidate interfaces (e.g., `Author` → Person rather than
    /// Organization, `Publisher` → Organization rather than Person).
    /// </summary>
    private static Thing? TryWrapScalarAsThing(Type targetType, string propertyName, string scalarValue)
    {
        var candidates = CollectCandidateThingInterfaces(targetType);
        if (candidates.Count == 0)
            return null;

        var concreteType = ChooseConcreteThingType(candidates, propertyName);
        if (concreteType is null)
            return null;

        if (Activator.CreateInstance(concreteType) is not Thing thing)
            return null;

        // Name is OneOrMany<Values<string>> on every Schema.org Thing — set it via
        // the recursive path so the existing implicit-conversion handling runs.
        SetPropertyValue(thing, "Name", scalarValue);
        return thing;
    }

    /// <summary>
    /// Builds an @id-only cross-reference value typed to match the target
    /// property's Schema.org range. A graph reference is a Thing carrying only
    /// <c>@id</c>, but narrowly-typed properties would silently drop a bare
    /// <see cref="Thing"/>: e.g. <c>Article.publisher</c> accepts
    /// <see cref="IOrganization"/>, so a plain Thing never binds and the
    /// publisher vanishes. Pick the concrete type from the property's range
    /// using the same name heuristic as scalar auto-wrapping (publisher →
    /// Organization, author → Person). Falls back to a bare Thing when the
    /// range already accepts <see cref="IThing"/> (e.g. about/mainEntity) or
    /// can't be resolved. GraphGenerator's ref-collapse then reduces the
    /// serialised <c>{@type,@id}</c> to <c>{@id}</c>.
    /// </summary>
    internal static Thing CreateReferenceShell(Thing instance, string propertyName, Uri id)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is { CanWrite: true })
        {
            var candidates = CollectCandidateThingInterfaces(property.PropertyType);
            if (ChooseConcreteThingType(candidates, propertyName) is { } concreteType
                && Activator.CreateInstance(concreteType) is Thing typed)
            {
                typed.Id = id;
                return typed;
            }
        }

        return new Thing { Id = id };
    }

    /// <summary>
    /// Chooses a concrete Schema.NET type to instantiate for a property mapping.
    /// Picks the best match from the candidate interfaces using a small property-name
    /// heuristic, then falls back to the first candidate.
    /// </summary>
    private static Type? ChooseConcreteThingType(List<Type> candidateInterfaces, string propertyName)
    {
        static Type? InterfaceToConcrete(Type iface)
        {
            var name = iface.Name;
            if (name.Length < 2 || name[0] != 'I')
                return null;
            var concreteName = name[1..];
            return iface.Assembly.GetType($"{iface.Namespace}.{concreteName}");
        }

        // Property-name → preferred concrete type. Matches suffix (case-insensitive)
        // so nested property paths like `author`, `mainAuthor`, `articleAuthor` all match.
        var preferred = propertyName.ToLowerInvariant() switch
        {
            var n when n.EndsWith("author") => "Person",
            var n when n.EndsWith("publisher") => "Organization",
            var n when n.EndsWith("provider") => "Organization",
            var n when n.EndsWith("manufacturer") => "Organization",
            var n when n.EndsWith("organizer") => "Organization",
            var n when n.EndsWith("sponsor") => "Organization",
            var n when n.EndsWith("brand") => "Brand",
            _ => null
        };

        if (preferred is not null)
        {
            var preferredConcrete = candidateInterfaces
                .Select(InterfaceToConcrete)
                .FirstOrDefault(concrete => concrete is not null && concrete.Name == preferred);
            if (preferredConcrete is not null)
                return preferredConcrete;
        }

        // Fallback: first candidate that resolves to a concrete type.
        return candidateInterfaces
            .Select(InterfaceToConcrete)
            .FirstOrDefault(concrete => concrete is not null);
    }

    /// <summary>
    /// Attempts to set a collection of Thing instances on a OneOrMany property.
    /// </summary>
    public static bool TrySetCollectionValue(PropertyInfo property, Thing instance, Type targetType, IEnumerable<Thing> things)
    {
        var thingList = things.ToList();
        if (thingList.Count == 0)
            return false;

        if (targetType is not { IsGenericType: true })
            return false;

        var genName = targetType.GetGenericTypeDefinition().Name;

        // Handle OneOrMany<T> — extract inner type and build collection
        if (genName.StartsWith("OneOrMany"))
        {
            var innerType = targetType.GetGenericArguments()[0];
            return TryBuildAndSetCollection(property, instance, targetType, innerType, thingList);
        }

        // Handle Values<T1,T2,...> directly — some Schema.NET properties use this without OneOrMany wrapper
        // Values has implicit operators for List<T> and T[] for each type argument
        if (genName.StartsWith("Values"))
        {
            // Find which interface type argument the Things implement
            var matchingInterfaceType = targetType.GetGenericArguments()
                .FirstOrDefault(t => t.IsInterface && t.IsAssignableFrom(thingList[0].GetType()));

            if (matchingInterfaceType is not null)
            {
                // Build a typed List<IFoo> and use the op_Implicit(List<IFoo>) operator
                var typedListType = typeof(List<>).MakeGenericType(matchingInterfaceType);
                var typedItemList = (IList)Activator.CreateInstance(typedListType)!;
                foreach (var thing in thingList.Where(t => matchingInterfaceType.IsAssignableFrom(t.GetType())))
                    typedItemList.Add(thing);

                if (typedItemList.Count > 0)
                {
                    var valuesConverted = TryConvertViaImplicit(targetType, typedItemList);
                    if (valuesConverted is not null)
                    {
                        property.SetValue(instance, valuesConverted);
                        return true;
                    }
                }
            }

            // Fallback for single item
            if (thingList.Count == 1)
            {
                var converted = TryConvertViaImplicit(targetType, thingList[0]);
                if (converted is not null)
                {
                    property.SetValue(instance, converted);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryBuildAndSetCollection(PropertyInfo property, Thing instance, Type targetType, Type innerType, List<Thing> thingList)
    {
        // Build a properly typed List<T> where T is the inner type of OneOrMany<T>
        var listType = typeof(List<>).MakeGenericType(innerType);
        var typedList = (IList)Activator.CreateInstance(listType)!;

        foreach (var converted in thingList
                     .Select(thing => TryConvertViaImplicit(innerType, thing))
                     .Where(converted => converted is not null))
        {
            typedList.Add(converted);
        }

        if (typedList.Count == 0)
            return false;

        // OneOrMany has constructors: (object[] items), (IEnumerable<object> items)
        // Use explicit constructor lookup and invocation
        var ctor = targetType.GetConstructor([typeof(object[])]);
        if (ctor is not null)
        {
            var objectArray = typedList.Cast<object>().ToArray();
            var oneOrManyInstance = ctor.Invoke([objectArray]);
            property.SetValue(instance, oneOrManyInstance);
            return true;
        }

        // Fallback: try Activator
        return TryConstructAndSet(property, instance, targetType, typedList);
    }

    /// <summary>
    /// Attempts to set a collection of strings on a OneOrMany property (e.g., recipeIngredient).
    /// Converts each string via implicit operators to build the inner Values type, then wraps in OneOrMany.
    /// </summary>
    public static bool TrySetStringCollectionValue(PropertyInfo property, Thing instance, Type targetType, IEnumerable<string> strings)
    {
        var stringList = strings.ToList();
        if (stringList.Count == 0)
            return false;

        if (targetType is not { IsGenericType: true })
            return false;

        var genDef = targetType.GetGenericTypeDefinition().Name;
        if (!genDef.StartsWith("OneOrMany"))
            return false;

        var innerType = targetType.GetGenericArguments()[0];

        // Uri inner types (e.g. Organization.SameAs is OneOrMany<Uri>) have no
        // string→Uri op_Implicit, so parse each string instead — mirroring the
        // single-string path in TrySetOneOrManyValue.
        object? ConvertItem(string s) => innerType == typeof(Uri)
            ? Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var uri) ? uri : null
            : TryConvertViaImplicit(innerType, s);

        // Build a list of inner-type values by converting each string
        var firstConverted = ConvertItem(stringList[0]);
        if (firstConverted is null)
            return false;

        var listType = typeof(List<>).MakeGenericType(innerType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(firstConverted);

        for (var i = 1; i < stringList.Count; i++)
        {
            var itemConverted = ConvertItem(stringList[i]);
            if (itemConverted is not null)
                list.Add(itemConverted);
        }

        return TryConstructAndSet(property, instance, targetType, list);
    }

    /// <summary>
    /// Attempts to set a single value on a OneOrMany property by building from inside out.
    /// Handles both OneOrMany&lt;Values&lt;T1,T2&gt;&gt; and simple OneOrMany&lt;T&gt; (e.g., OneOrMany&lt;Uri&gt;).
    /// </summary>
    public static bool TrySetOneOrManyValue(PropertyInfo property, Thing instance, Type targetType, object value)
    {
        var innerType = targetType.GetGenericArguments()[0];

        // Handle OneOrMany<Values<T1,T2,...>> — the most common Schema.NET pattern.
        // Build the inner Values<> (implicit + string coercions), then wrap it in OneOrMany<>
        // via implicit-operator-then-constructor. When both wrapping paths fail we fall through
        // to the simpler OneOrMany<T> / general handling below.
        if (innerType is { IsGenericType: true } && innerType.GetGenericTypeDefinition().Name.StartsWith("Values"))
        {
            var valuesArgs = innerType.GetGenericArguments();
            var valuesInstance = TryBuildValues(innerType, valuesArgs, value);
            if (valuesInstance is not null && TryWrapAndSet(property, instance, targetType, valuesInstance))
                return true;
        }

        // Handle simple OneOrMany<T> where T is not Values<> (e.g., OneOrMany<Uri>)
        if (value is string strValue && innerType == typeof(Uri)
            && Uri.TryCreate(strValue, UriKind.RelativeOrAbsolute, out var directUri)
            && TrySetViaImplicit(property, instance, targetType, directUri))
            return true;

        // General fallback: try converting value directly to OneOrMany<T> via T
        var directConverted = TryConvertViaImplicit(innerType, value);
        return directConverted is not null
               && TrySetViaImplicit(property, instance, targetType, directConverted);
    }

    /// <summary>
    /// Builds a Schema.NET <c>Values&lt;…&gt;</c> instance (the inner type of a
    /// <c>OneOrMany&lt;Values&lt;…&gt;&gt;</c>) from a resolved value. First tries a direct
    /// implicit conversion; then, for a string value, the string-specific coercions IN ORDER:
    /// string-typed arg → Uri (RelativeOrAbsolute) → date/number. Returns <c>null</c> when none apply.
    /// </summary>
    private static object? TryBuildValues(Type innerType, Type[] valuesArgs, object value)
    {
        // Build Values<> via implicit operator
        var valuesInstance = TryConvertViaImplicit(innerType, value);

        // If value is a string, try string-specific conversions
        if (valuesInstance is null && value is string stringValue)
        {
            if (valuesArgs.Any(t => t == typeof(string)))
            {
                valuesInstance = TryConvertViaImplicit(innerType, stringValue);
            }

            if (valuesInstance is null && valuesArgs.Any(t => t == typeof(Uri))
                && Uri.TryCreate(stringValue, UriKind.RelativeOrAbsolute, out var uri))
            {
                valuesInstance = TryConvertViaImplicit(innerType, uri);
            }

            // Date/number-typed Values inside OneOrMany (mirrors the bare-Values path)
            if (valuesInstance is null
                && TryParseDateOrNumber(valuesArgs, stringValue, out var parsed)
                && parsed is not null)
            {
                valuesInstance = TryConvertViaImplicit(innerType, parsed);
            }
        }

        return valuesInstance;
    }

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/> via an implicit
    /// operator and, on success, assigns it to <paramref name="property"/>. Returns <c>false</c>
    /// (leaving the property untouched) when no implicit conversion applies.
    /// </summary>
    private static bool TrySetViaImplicit(PropertyInfo property, Thing instance, Type targetType, object value)
    {
        var converted = TryConvertViaImplicit(targetType, value);
        if (converted is null)
            return false;

        property.SetValue(instance, converted);
        return true;
    }

    /// <summary>
    /// Wraps <paramref name="value"/> into <paramref name="targetType"/> and assigns it, trying
    /// the implicit operator first and then the Activator constructor (the
    /// implicit-operator-then-Activator-constructor pattern). Returns <c>false</c> when both fail.
    /// </summary>
    private static bool TryWrapAndSet(PropertyInfo property, Thing instance, Type targetType, object value)
        => TrySetViaImplicit(property, instance, targetType, value)
           || TryConstructAndSet(property, instance, targetType, value);

    /// <summary>
    /// Constructs <paramref name="targetType"/> from a single argument via
    /// <see cref="Activator.CreateInstance(Type, object[])"/> and assigns it. Returns <c>false</c>
    /// on <see cref="MissingMethodException"/> / <see cref="TargetInvocationException"/> (no
    /// matching constructor) so callers can fall through. Shared by the OneOrMany/collection builders.
    /// </summary>
    private static bool TryConstructAndSet(PropertyInfo property, Thing instance, Type targetType, object arg)
    {
        try
        {
            var created = Activator.CreateInstance(targetType, arg);
            property.SetValue(instance, created);
            return true;
        }
        catch (MissingMethodException)
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to set a value on a Values&lt;T1, T2, ...&gt; property.
    /// Handles string-to-Uri conversion when Uri is one of the type arguments.
    /// </summary>
    public static bool TrySetValuesValue(PropertyInfo property, Thing instance, Type targetType, object value)
    {
        if (value is not string stringValue)
            return false;

        var valuesArgs = targetType.GetGenericArguments();

        // Date/number type arguments (e.g. datePublished is Values<int?, DateTime?, DateTimeOffset?>).
        // The resolved value is a string (ISO from DateTimeResolver, or a formatDate transform result),
        // and Schema.NET exposes no string→DateTimeOffset implicit operator, so we parse it here.
        if (TryParseDateOrNumber(valuesArgs, stringValue, out var parsed)
            && parsed is not null)
        {
            var convertedDate = TryConvertViaImplicit(targetType, parsed);
            if (convertedDate is not null)
            {
                property.SetValue(instance, convertedDate);
                return true;
            }
        }

        // If Uri is one of the type arguments, try converting the string to Uri
        if (valuesArgs.Any(t => t == typeof(Uri))
            && Uri.TryCreate(stringValue, UriKind.RelativeOrAbsolute, out var uri))
        {
            var converted = TryConvertViaImplicit(targetType, uri);
            if (converted is not null)
            {
                property.SetValue(instance, converted);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a string into the most appropriate CLR value accepted by a Schema.NET
    /// <c>Values&lt;…&gt;</c> whose type arguments include date and/or integer types.
    /// A string carrying an explicit timezone offset (or <c>Z</c>) becomes a
    /// <see cref="DateTimeOffset"/> to preserve it; a zone-less date (e.g. <c>"2026-06-29"</c>)
    /// becomes a <see cref="DateTime"/> so no spurious server-local offset is introduced.
    /// An all-digit string with no date component falls back to <see cref="int"/> (e.g. a year).
    /// Returns false when none of those CLR types are among the Values type arguments or the
    /// string parses to none of them.
    /// </summary>
    internal static bool TryParseDateOrNumber(IReadOnlyList<Type> valuesArgs, string value, out object? parsed)
    {
        parsed = null;

        bool Accepts<T>() => valuesArgs.Any(t => (Nullable.GetUnderlyingType(t) ?? t) == typeof(T));

        var acceptsDateTimeOffset = Accepts<DateTimeOffset>();
        var acceptsDateTime = Accepts<DateTime>();
        var acceptsInt = Accepts<int>();

        // RoundtripKind keeps the parsed Kind faithful to the input: Utc/Local when the
        // string carried a zone, Unspecified when it didn't.
        if ((acceptsDateTimeOffset || acceptsDateTime)
            && DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
        {
            var hadOffset = dt.Kind != DateTimeKind.Unspecified;
            if (hadOffset && acceptsDateTimeOffset
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dto))
            {
                parsed = dto;
                return true;
            }

            if (acceptsDateTime)
            {
                parsed = dt;
                return true;
            }

            if (acceptsDateTimeOffset
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dtoFallback))
            {
                parsed = dtoFallback;
                return true;
            }
        }

        if (acceptsInt && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            parsed = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to convert a value to the target type using op_Implicit operators.
    /// Searches both the target type and the source type for matching operators.
    /// </summary>
    public static object? TryConvertViaImplicit(Type targetType, object value)
    {
        // If the value is already assignable to the target type, return it directly
        if (targetType.IsInstanceOfType(value))
            return value;

        // Search for op_Implicit on the target type
        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1
                && ParameterAccepts(m.GetParameters()[0].ParameterType, value.GetType()));

        foreach (var method in methods)
        {
            try
            {
                return method.Invoke(null, [value]);
            }
            catch (TargetInvocationException)
            {
                // Continue trying other conversions
            }
        }

        // Also search on the source type for op_Implicit returning targetType
        var sourceMethods = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.ReturnType == targetType && m.GetParameters().Length == 1
                && ParameterAccepts(m.GetParameters()[0].ParameterType, value.GetType()));

        foreach (var method in sourceMethods)
        {
            try
            {
                return method.Invoke(null, [value]);
            }
            catch (TargetInvocationException)
            {
                // Continue trying other conversions
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an <c>op_Implicit</c> parameter of <paramref name="parameterType"/> can accept a
    /// value of <paramref name="valueType"/>. Beyond a direct assignability check this unwraps
    /// <see cref="Nullable{T}"/> parameters so that, e.g., a <see cref="DateTimeOffset"/> value
    /// matches an <c>op_Implicit(DateTimeOffset?)</c> operator. Schema.NET's date/number value
    /// types (e.g. <c>Values&lt;int?, DateTime?, DateTimeOffset?&gt;</c>) expose only nullable
    /// operator overloads, so without this unwrapping every parsed date would be dropped.
    /// </summary>
    private static bool ParameterAccepts(Type parameterType, Type valueType)
    {
        if (parameterType.IsAssignableFrom(valueType))
            return true;

        var underlying = Nullable.GetUnderlyingType(parameterType);
        return underlying is not null && underlying.IsAssignableFrom(valueType);
    }

    /// <summary>
    /// Resolves a property value from an <see cref="IPublishedElement"/>, handling special types
    /// like media pickers (which return <see cref="MediaWithCrops"/> instead of a URL string).
    /// </summary>
    public static object? ResolveElementPropertyValue(
        IPublishedElement element,
        string propertyAlias,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var prop = element.GetProperty(propertyAlias);
        if (prop is null)
            return null;

        var value = prop.GetValue();
        if (value is null)
            return null;

        // Check if this is a media picker property by editor alias
        var editorAlias = prop.PropertyType?.EditorAlias;
        if (editorAlias is not null && SchemeWeaverConstants.PropertyEditors.MediaPickerAliases.Contains(editorAlias))
        {
            // Media pickers must NEVER fall through to value.ToString() below — that would
            // leak raw MediaWithCrops JSON. Return the extracted URL (which may be null when
            // the picker is empty or the media has no file) directly.
            return TryExtractMediaUrl(value, httpContextAccessor);
        }

        var stringValue = value.ToString();
        return string.IsNullOrWhiteSpace(stringValue) ? null : stringValue;
    }

    /// <summary>
    /// Extracts a media URL from a media picker property value (MediaWithCrops or IPublishedContent).
    /// </summary>
    private static string? TryExtractMediaUrl(object value, IHttpContextAccessor? httpContextAccessor)
    {
        IPublishedContent? mediaContent = value switch
        {
            MediaWithCrops single => single.Content,
            IEnumerable<MediaWithCrops> multiple => multiple.FirstOrDefault()?.Content,
            IPublishedContent content => content,
            IEnumerable<IPublishedContent> contents => contents.FirstOrDefault(),
            _ => null
        };

        if (mediaContent is null)
            return null;

        var umbracoFile = mediaContent.GetProperty("umbracoFile");
        var fileValue = umbracoFile?.GetValue();
        if (fileValue is null)
            return null;

        var relativeUrl = fileValue is ImageCropperValue cropperValue
            ? cropperValue.Src
            : fileValue.ToString();

        if (string.IsNullOrEmpty(relativeUrl))
            return null;

        if (relativeUrl!.StartsWith('/') && httpContextAccessor?.HttpContext?.Request is { } request)
            return $"{request.Scheme}://{request.Host}{relativeUrl}";

        return relativeUrl;
    }
}
