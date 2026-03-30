using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
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
    public static void SetPropertyValue(Thing instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // If the value is already the correct type, set directly
        if (targetType.IsInstanceOfType(value))
        {
            property.SetValue(instance, value);
            return;
        }

        // Try to find an implicit conversion operator that accepts our value type
        var converted = TryConvertViaImplicit(targetType, value);
        if (converted is not null)
        {
            property.SetValue(instance, converted);
            return;
        }

        // Handle IEnumerable<Thing> for collection properties (e.g., block content results)
        if (value is IEnumerable<Thing> things)
        {
            if (TrySetCollectionValue(property, instance, targetType, things))
                return;
        }

        // Handle IEnumerable<string> for string array properties (e.g., recipeIngredient)
        if (value is IEnumerable<string> strings)
        {
            if (TrySetStringCollectionValue(property, instance, targetType, strings))
                return;
        }

        // Handle OneOrMany<T> types from Schema.NET by building from inside out
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany"))
        {
            if (TrySetOneOrManyValue(property, instance, targetType, value))
                return;
        }

        // Handle Values<T1, T2, ...> types directly (e.g., Image is Values<IImageObject, Uri>)
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("Values"))
        {
            if (TrySetValuesValue(property, instance, targetType, value))
                return;
        }

        // Simple string assignment
        if (targetType == typeof(string) && value is string strVal)
        {
            property.SetValue(instance, strVal);
        }
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
        catch
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
        catch
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
                catch
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
            .Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1);

        foreach (var method in methods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(value.GetType()))
                continue;

            try
            {
                return method.Invoke(null, [value]);
            }
            catch
            {
                // Continue trying other conversions
            }
        }

        // Also search on the source type for op_Implicit returning targetType
        var sourceMethods = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.ReturnType == targetType && m.GetParameters().Length == 1);

        foreach (var method in sourceMethods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(value.GetType()))
                continue;

            try
            {
                return method.Invoke(null, [value]);
            }
            catch
            {
                // Continue trying other conversions
            }
        }

        return null;
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
            var mediaUrl = TryExtractMediaUrl(value, httpContextAccessor);
            if (mediaUrl is not null)
                return mediaUrl;
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
