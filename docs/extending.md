# Extending SchemeWeaver

SchemeWeaver is designed to be extensible. Every core service is registered against an interface via dependency injection, so you can replace or augment any part of the pipeline. This guide covers all extension points, from the most common (custom property resolvers) to full service replacement.

---

## Architecture Overview

SchemeWeaver's DI registrations in `SchemeWeaverComposer`:

| Interface | Built-in Implementation | Scope | Extension Pattern |
|---|---|---|---|
| `IPropertyValueResolver` | 14 resolvers | Scoped | **Add alongside** (multicast) |
| `IPropertyValueResolverFactory` | `PropertyValueResolverFactory` | Scoped | Replace |
| `ISchemaAutoMapper` | `SchemaAutoMapper` | Scoped | Replace |
| `IJsonLdGenerator` | `JsonLdGenerator` | Scoped | Replace |
| `ISchemaTypeRegistry` | `SchemaTypeRegistry` | Singleton | Replace |
| `ISchemaMappingRepository` | `SchemaMappingRepository` | Scoped | Replace |
| `IContentTypeGenerator` | `ContentTypeGenerator` | Scoped | Replace |
| `ISchemeWeaverService` | `SchemeWeaverService` | Scoped | Replace |

The key distinction: `IPropertyValueResolver` is **multicast** (you register additional implementations alongside the built-in ones), while every other service is **replaceable** (your registration takes precedence over SchemeWeaver's).

---

## Custom Property Value Resolvers

This is the most common extension point. Property resolvers control **how** a value is extracted from an Umbraco property and transformed into a Schema.NET-compatible object.

### The IPropertyValueResolver Interface

```csharp
public interface IPropertyValueResolver
{
    /// The Umbraco property editor aliases this resolver handles.
    /// Return empty to act as a fallback resolver.
    IEnumerable<string> SupportedEditorAliases { get; }

    /// Priority for resolver selection when multiple resolvers match.
    /// Higher values take precedence. Default is 0.
    int Priority => 0;

    /// Resolves the property value to a type suitable for Schema.NET.
    object? Resolve(PropertyResolverContext context);
}
```

### How Resolvers Are Selected

The `PropertyValueResolverFactory` collects all registered `IPropertyValueResolver` implementations from DI and builds a lookup:

1. Sorts all resolvers by `Priority` descending.
2. For each editor alias in `SupportedEditorAliases`, maps it to the resolver. If multiple resolvers claim the same alias, the highest priority wins.
3. The first resolver with an empty `SupportedEditorAliases` list becomes the fallback (used when no alias-specific resolver matches).

At runtime, when a property needs resolving, the factory looks up the resolver by the property's editor alias. If none matches, the fallback resolver is used.

### Writing a Custom Resolver

Example: a resolver for a hypothetical "Star Rating" property editor that outputs a Schema.org `Rating` object.

```csharp
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

public class StarRatingResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["MyPackage.StarRating"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is not int rating)
            return null;

        return new Schema.NET.Rating
        {
            RatingValue = rating,
            BestRating = 5,
            WorstRating = 1
        };
    }
}
```

### Registering a Custom Resolver

Register your resolver in a composer. Because `IPropertyValueResolver` is multicast, your resolver is added alongside the built-in ones:

```csharp
public class MyResolverComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IPropertyValueResolver, StarRatingResolver>();
    }
}
```

### Overriding a Built-in Resolver

To override how an existing editor type is resolved (e.g. to change how `Umbraco.MediaPicker3` values are extracted), register your resolver with a higher priority than the built-in one (which uses priority 10):

```csharp
public class CustomMediaResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MediaPicker3", "Umbraco.MediaPicker"];

    public int Priority => 20; // Higher than the built-in MediaPickerResolver (10)

    public object? Resolve(PropertyResolverContext context)
    {
        // Your custom logic here
    }
}
```

### PropertyResolverContext

The context object passed to every resolver:

| Property | Type | Description |
|---|---|---|
| `Content` | `IPublishedContent` | The content node (already resolved to the correct node by the source type). |
| `Mapping` | `PropertyMapping` | The property mapping configuration from the database. |
| `PropertyAlias` | `string` | The alias of the property to resolve. |
| `Property` | `IPublishedProperty?` | The raw published property. May be null for built-in properties (`__url`, `__name`, etc.). |
| `SchemaTypeRegistry` | `ISchemaTypeRegistry` | For looking up nested Schema.org types. |
| `MappingRepository` | `ISchemaMappingRepository` | For looking up nested schema mappings. |
| `HttpContextAccessor` | `IHttpContextAccessor` | For resolving absolute URLs. |
| `ResolverFactory` | `IPropertyValueResolverFactory?` | For recursively resolving nested properties. |
| `RecursionDepth` | `int` | Current recursion depth (max 3 by default). |
| `MaxRecursionDepth` | `int` | Maximum recursion depth. |
| `VisitedContentKeys` | `HashSet<Guid>` | Tracks visited content keys to prevent circular references. |

### Built-in Resolvers

| Resolver | Editor Aliases | Priority | Behaviour |
|---|---|---|---|
| `MediaPickerResolver` | `Umbraco.MediaPicker3`, `Umbraco.MediaPicker` | 10 | Extracts absolute media URL |
| `RichTextResolver` | `Umbraco.TinyMCE`, `Umbraco.RichText`, `Umbraco.MarkdownEditor` | 10 | Strips HTML or returns raw |
| `ContentPickerResolver` | `Umbraco.ContentPicker` | 10 | Resolves picked content URL; tracks visited keys to prevent circular references |
| `BlockContentResolver` | `Umbraco.BlockList`, `Umbraco.BlockGrid` | 10 | Resolves block element schemas using nested mappings from `ResolverConfig` |
| `BuiltInPropertyResolver` | `SchemeWeaver.BuiltIn` | 10 | Handles `__url`, `__name`, `__createDate`, `__updateDate` |
| `DateTimeResolver` | `Umbraco.DateTime` | 10 | Formats as ISO 8601 (`yyyy-MM-dd`) |
| `NumericResolver` | `Umbraco.Integer`, `Umbraco.Decimal` | 10 | Preserves numeric type for JSON serialisation |
| `BooleanResolver` | `Umbraco.TrueFalse` | 10 | Returns boolean |
| `TagsResolver` | `Umbraco.Tags` | 10 | Returns comma-separated string |
| `MultipleTextStringResolver` | `Umbraco.MultipleTextstring` | 10 | Returns `List<string>` |
| `DropdownListResolver` | `Umbraco.DropDown.Flexible` | 10 | Returns selected value |
| `ColorPickerResolver` | `Umbraco.ColorPicker` | 10 | Returns colour value |
| `MultiUrlPickerResolver` | `Umbraco.MultiUrlPicker` | 10 | Resolves first URL |
| `DefaultPropertyValueResolver` | *(fallback -- empty aliases)* | 0 | Calls `GetValue()?.ToString()` |

---

## Replacing the Auto-Mapper

The `ISchemaAutoMapper` controls how property mapping suggestions are generated when a user clicks "Auto-map".

```csharp
public interface ISchemaAutoMapper
{
    IEnumerable<PropertyMappingSuggestion> SuggestMappings(
        string contentTypeAlias, string schemaTypeName);
}
```

The built-in `SchemaAutoMapper` uses a three-tier matching algorithm (exact name, synonym dictionary, substring) with pre-built defaults for common schema types like `FAQPage`, `Product`, and `Recipe`.

To replace it:

```csharp
public class MyAutoMapper : ISchemaAutoMapper
{
    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(
        string contentTypeAlias, string schemaTypeName)
    {
        // Your custom matching logic
    }
}

public class MyAutoMapperComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<ISchemaAutoMapper, MyAutoMapper>();
    }
}
```

Your registration runs after SchemeWeaver's `SchemeWeaverComposer`, so it replaces the built-in implementation.

---

## Replacing the JSON-LD Generator

The `IJsonLdGenerator` controls the runtime JSON-LD output -- both for the tag helper and the Delivery API.

```csharp
public interface IJsonLdGenerator
{
    Thing? GenerateJsonLd(IPublishedContent content);
    string? GenerateJsonLdString(IPublishedContent content);
    string? GenerateBreadcrumbJsonLd(IPublishedContent content);
    IEnumerable<string> GenerateInheritedJsonLdStrings(IPublishedContent content);
    IEnumerable<string> GenerateBlockElementJsonLdStrings(IPublishedContent content);
}
```

Use cases for replacement:
- Adding custom post-processing to the generated JSON-LD (e.g. injecting `@id` values)
- Changing how inherited schemas are resolved
- Customising the BreadcrumbList generation logic
- Adding validation or sanitisation before output

```csharp
public class MyJsonLdGenerator : IJsonLdGenerator
{
    private readonly JsonLdGenerator _inner;

    public MyJsonLdGenerator(/* same deps as JsonLdGenerator */)
    {
        // Delegate to the built-in implementation for base behaviour
    }

    public string? GenerateJsonLdString(IPublishedContent content)
    {
        var json = _inner.GenerateJsonLdString(content);
        // Post-process the JSON-LD
        return json;
    }

    // ... implement remaining methods
}
```

---

## Replacing the Schema Type Registry

The `ISchemaTypeRegistry` provides the list of available Schema.org types. The built-in implementation scans the Schema.NET.Pending assembly at startup.

```csharp
public interface ISchemaTypeRegistry
{
    IEnumerable<SchemaTypeInfo> GetAllTypes();
    SchemaTypeInfo? GetType(string name);
    IEnumerable<SchemaPropertyInfo> GetProperties(string typeName);
    IEnumerable<SchemaTypeInfo> Search(string query);
    Type? GetClrType(string typeName);
}
```

Use cases for replacement:
- Filtering the available types to a subset relevant to your site
- Adding custom types that are not in the Schema.NET.Pending library
- Loading type definitions from a different source (e.g. a remote API)

**Note:** The registry is registered as a **singleton**, so your replacement must also be thread-safe.

---

## Replacing the Mapping Repository

The `ISchemaMappingRepository` controls how mappings are persisted. The built-in implementation uses Umbraco's NPoco database layer.

```csharp
public interface ISchemaMappingRepository
{
    IEnumerable<SchemaMapping> GetAll();
    SchemaMapping? GetByContentTypeAlias(string contentTypeAlias);
    SchemaMapping Save(SchemaMapping mapping);
    void Delete(int id);
    IEnumerable<PropertyMapping> GetPropertyMappings(int schemaMappingId);
    void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings);
    IEnumerable<SchemaMapping> GetInheritedMappings();
}
```

Use cases for replacement:
- Storing mappings in a different database or external service
- Adding caching over the default implementation
- Syncing mappings across environments

---

## Replacing the Content Type Generator

The `IContentTypeGenerator` creates Umbraco document types from Schema.org type definitions.

```csharp
public interface IContentTypeGenerator
{
    Task<Guid> GenerateContentTypeAsync(
        ContentTypeGenerationRequest request,
        CancellationToken cancellationToken = default);
}
```

The built-in implementation maps Schema.org property types to Umbraco property editors using a predefined dictionary (e.g. `Text` → `Umbraco.TextBox`, `URL` → `Umbraco.TextBox`, `DateTime` → `Umbraco.DateTime`). Replacing it lets you customise the editor mapping or the generated document type structure.

---

## Registration Order

SchemeWeaver registers its services in `SchemeWeaverComposer`, which implements `IComposer`. Umbraco runs composers in dependency order. If your composer does not declare a dependency on `SchemeWeaverComposer`, the order is not guaranteed. To ensure your registrations run after SchemeWeaver's:

```csharp
[ComposeAfter(typeof(SchemeWeaverComposer))]
public class MyComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Your replacement registrations here
    }
}
```

For multicast services like `IPropertyValueResolver`, ordering does not matter -- all registrations are collected by the factory regardless of order.

---

## Further Reading

- **[Property Mappings](property-mappings.md)** -- source types, transforms, and the auto-mapping algorithm
- **[Block Content](block-content.md)** -- how BlockContentResolver and nested mappings work
- **[API Reference](api-reference.md)** -- REST API endpoints
- **[AI Integration](ai-integration.md)** -- optional AI-powered mapping via the companion package
