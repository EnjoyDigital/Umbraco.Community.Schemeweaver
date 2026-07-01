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

        // If the value is already the correct type, set directly
        if (targetType.IsInstanceOfType(value))
        {
            property.SetValue(instance, value);
            return;
        }

        // Media values now arrive as Schema.NET ImageObject(s). When the target accepts
        // IImageObject (e.g. Article.Image, Organization.Logo) we fall through to the normal
        // OneOrMany/Values/collection handling below, which sets the ImageObject(s) directly.
        // But some targets accept only a Uri leaf (e.g. Thing.Url, contentUrl, sameAs) and NOT
        // IImageObject — an ImageObject would otherwise be dropped. Downgrade it to its URL.
        if (IsImageObjectValue(value)
            && !TargetAcceptsLeaf(targetType, typeof(IImageObject))
            && TargetAcceptsLeaf(targetType, typeof(Uri)))
        {
            var downgraded = DowngradeImageToUri(value);
            if (downgraded is not null)
            {
                SetPropertyValue(instance, propertyName, downgraded, logger);
                return;
            }
        }

        // Try to find an implicit conversion operator that accepts our value type
        var converted = TryConvertViaImplicit(targetType, value);
        if (converted is not null)
        {
            property.SetValue(instance, converted);
            return;
        }

        // Handle IEnumerable<Thing> for collection properties (e.g., block content results)
        if (value is IEnumerable<Thing> things
            && TrySetCollectionValue(property, instance, targetType, things))
            return;

        // Handle IEnumerable<string> for string array properties (e.g., recipeIngredient)
        if (value is IEnumerable<string> strings
            && TrySetStringCollectionValue(property, instance, targetType, strings))
            return;

        // Handle OneOrMany<T> types from Schema.NET by building from inside out
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany")
            && TrySetOneOrManyValue(property, instance, targetType, value))
            return;

        // Handle Values<T1, T2, ...> types directly (e.g., Image is Values<IImageObject, Uri>)
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("Values")
            && TrySetValuesValue(property, instance, targetType, value))
            return;

        // Simple string assignment
        if (targetType == typeof(string) && value is string strVal)
        {
            property.SetValue(instance, strVal);
            return;
        }

        // Auto-wrap scalar strings into a concrete Thing for object-typed properties.
        // e.g. `Brand` mapped from a Textbox → { "@type": "Brand", "name": "AudioTech" }.
        // Users very commonly map Schema.org object properties (Brand, Author, Publisher)
        // from a plain string field; without this fallback Schema.NET silently drops the
        // value because no implicit conversion from string to IBrand/IPerson exists.
        if (value is string scalarString && !string.IsNullOrWhiteSpace(scalarString))
        {
            var wrapped = TryWrapScalarAsThing(targetType, propertyName, scalarString);
            if (wrapped is not null)
            {
                // Re-enter the main setter with the wrapped Thing — it will match the
                // normal Thing-handling paths (implicit conversion / OneOrMany / Values).
                SetPropertyValue(instance, propertyName, wrapped, logger);
                return;
            }
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
            foreach (var iface in candidateInterfaces)
            {
                var concrete = InterfaceToConcrete(iface);
                if (concrete is not null && concrete.Name == preferred)
                    return concrete;
            }
        }

        // Fallback: first candidate that resolves to a concrete type.
        foreach (var iface in candidateInterfaces)
        {
            var concrete = InterfaceToConcrete(iface);
            if (concrete is not null)
                return concrete;
        }

        return null;
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
                foreach (var thing in thingList)
                {
                    if (matchingInterfaceType.IsAssignableFrom(thing.GetType()))
                        typedItemList.Add(thing);
                }

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

        foreach (var thing in thingList)
        {
            var converted = TryConvertViaImplicit(innerType, thing);
            if (converted is not null)
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
        try
        {
            var oneOrMany = Activator.CreateInstance(targetType, typedList);
            property.SetValue(instance, oneOrMany);
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

        // Build a list of inner-type values by converting each string
        var firstConverted = TryConvertViaImplicit(innerType, stringList[0]);
        if (firstConverted is null)
            return false;

        var listType = typeof(List<>).MakeGenericType(innerType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(firstConverted);

        for (var i = 1; i < stringList.Count; i++)
        {
            var itemConverted = TryConvertViaImplicit(innerType, stringList[i]);
            if (itemConverted is not null)
                list.Add(itemConverted);
        }

        try
        {
            var oneOrManyInstance = Activator.CreateInstance(targetType, list);
            property.SetValue(instance, oneOrManyInstance);
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
    /// Attempts to set a single value on a OneOrMany property by building from inside out.
    /// Handles both OneOrMany&lt;Values&lt;T1,T2&gt;&gt; and simple OneOrMany&lt;T&gt; (e.g., OneOrMany&lt;Uri&gt;).
    /// </summary>
    public static bool TrySetOneOrManyValue(PropertyInfo property, Thing instance, Type targetType, object value)
    {
        var innerType = targetType.GetGenericArguments()[0];

        // Handle OneOrMany<Values<T1,T2,...>> — the most common Schema.NET pattern
        if (innerType is { IsGenericType: true } && innerType.GetGenericTypeDefinition().Name.StartsWith("Values"))
        {
            var valuesArgs = innerType.GetGenericArguments();

            // Build Values<> via implicit operator
            object? valuesInstance = TryConvertViaImplicit(innerType, value);

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

            if (valuesInstance is not null)
            {
                // Build OneOrMany<> from Values<>
                var oneOrMany = TryConvertViaImplicit(targetType, valuesInstance);
                if (oneOrMany is not null)
                {
                    property.SetValue(instance, oneOrMany);
                    return true;
                }

                // Try constructor
                try
                {
                    var oneOrManyInstance = Activator.CreateInstance(targetType, valuesInstance);
                    property.SetValue(instance, oneOrManyInstance);
                    return true;
                }
                catch (MissingMethodException)
                {
                    // Fall through
                }
                catch (TargetInvocationException)
                {
                    // Fall through
                }
            }
        }

        // Handle simple OneOrMany<T> where T is not Values<> (e.g., OneOrMany<Uri>)
        if (value is string strValue && innerType == typeof(Uri)
            && Uri.TryCreate(strValue, UriKind.RelativeOrAbsolute, out var directUri))
        {
            var oneOrMany = TryConvertViaImplicit(targetType, directUri);
            if (oneOrMany is not null)
            {
                property.SetValue(instance, oneOrMany);
                return true;
            }
        }

        // General fallback: try converting value directly to OneOrMany<T> via T
        var directConverted = TryConvertViaImplicit(innerType, value);
        if (directConverted is not null)
        {
            var oneOrMany = TryConvertViaImplicit(targetType, directConverted);
            if (oneOrMany is not null)
            {
                property.SetValue(instance, oneOrMany);
                return true;
            }
        }

        return false;
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
    internal static bool TryParseDateOrNumber(Type[] valuesArgs, string value, out object? parsed)
    {
        parsed = null;

        bool Accepts<T>() => valuesArgs.Any(t => (Nullable.GetUnderlyingType(t) ?? t) == typeof(T));

        var acceptsDateTimeOffset = Accepts<DateTimeOffset>();
        var acceptsDateTime = Accepts<DateTime>();
        var acceptsInt = Accepts<int>();

        if (acceptsDateTimeOffset || acceptsDateTime)
        {
            // RoundtripKind keeps the parsed Kind faithful to the input: Utc/Local when the
            // string carried a zone, Unspecified when it didn't.
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
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
        if (editorAlias is "Umbraco.MediaPicker3" or "Umbraco.MediaPicker")
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
