using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Seo.Templates;

/// <summary>
/// Loads <c>Templates/catalogue.json</c> once and validates it against the Schema.org registry so
/// a typo in the catalogue fails at start-up (and in the unit tests) rather than at render time.
/// </summary>
public sealed class SeoTemplateCatalogue : ISeoTemplateCatalogue
{
    public const string ResourceName = "Umbraco.Community.SchemeWeaver.Seo.Templates.catalogue.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ISchemaTypeRegistry _registry;
    private readonly Dictionary<string, SeoTemplate> _byId;
    private readonly Dictionary<string, SeoTemplate> _byOwnedType;

    public SeoTemplateCatalogue(ISchemaTypeRegistry registry, ILogger<SeoTemplateCatalogue> logger)
        : this(registry, LoadEmbedded(), logger)
    {
    }

    internal SeoTemplateCatalogue(ISchemaTypeRegistry registry, SeoCatalogueDocument document, ILogger<SeoTemplateCatalogue> logger)
    {
        _registry = registry;
        _registry.EnsureInitialised();

        ApplySlotKindDefaults(document);
        Validate(document);

        All = document.Templates;
        Retired = document.Retired;
        BusinessTypes = document.BusinessTypes;
        _byId = document.Templates.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        _byOwnedType = new Dictionary<string, SeoTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in document.Templates)
        {
            foreach (var type in template.OwnedSchemaTypes)
                _byOwnedType.TryAdd(type, template);

            if (template.UseBusinessTypes)
            {
                foreach (var businessType in document.BusinessTypes)
                    _byOwnedType.TryAdd(businessType.SchemaType, template);
            }
        }

        logger.LogDebug("SchemeWeaver SEO catalogue loaded: {TemplateCount} kinds, {RetiredCount} retired features", All.Count, Retired.Count);
    }

    public IReadOnlyList<SeoTemplate> All { get; }
    public IReadOnlyList<SeoRetiredFeature> Retired { get; }
    public IReadOnlyList<SeoBusinessType> BusinessTypes { get; }

    public SeoTemplate? Get(string id) =>
        !string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out var template) ? template : null;

    public SeoTemplate? FindForSchemaType(string schemaType)
    {
        if (string.IsNullOrWhiteSpace(schemaType))
            return null;

        if (_byOwnedType.TryGetValue(schemaType, out var exact))
            return exact;

        // Walk up the Schema.org hierarchy: a mapping to ScholarlyArticle still belongs to the
        // article kind even though the catalogue never names it.
        var current = _registry.GetType(schemaType);
        var guard = 0;
        while (current?.ParentTypeName is { Length: > 0 } parent && guard++ < 16)
        {
            if (_byOwnedType.TryGetValue(parent, out var ancestorTemplate)
                && string.Equals(ancestorTemplate.SchemaType, parent, StringComparison.OrdinalIgnoreCase))
            {
                return ancestorTemplate;
            }

            current = _registry.GetType(parent);
        }

        return null;
    }

    public IReadOnlyList<SeoTemplateVariant> AllowedPageTypes(SeoTemplate template)
    {
        if (template.UseBusinessTypes)
        {
            return BusinessTypes
                .Select(b => new SeoTemplateVariant { SchemaType = b.SchemaType, Label = b.Label })
                .ToList();
        }

        return template.Variants.Count > 0
            ? template.Variants
            : [new SeoTemplateVariant { SchemaType = template.SchemaType, Label = template.Label }];
    }

    internal static SeoCatalogueDocument LoadEmbedded()
    {
        using var stream = typeof(SeoTemplateCatalogue).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded catalogue '{ResourceName}' is missing from the SchemeWeaver SEO assembly.");
        return JsonSerializer.Deserialize<SeoCatalogueDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The SchemeWeaver SEO catalogue deserialised to null.");
    }

    private static void ApplySlotKindDefaults(SeoCatalogueDocument document)
    {
        foreach (var slot in document.Templates.SelectMany(t => t.Slots).SelectMany(Flatten))
        {
            if (!document.SlotKinds.TryGetValue(slot.Kind, out var defaults))
                throw new InvalidOperationException($"Catalogue slot '{slot.SchemaProperty}' uses unknown kind '{slot.Kind}'.");

            if (slot.AcceptedEditorAliases.Count == 0)
                slot.AcceptedEditorAliases = defaults.AcceptedEditorAliases;
            if (slot.BuiltIns.Count == 0)
                slot.BuiltIns = defaults.BuiltIns;
        }
    }

    private static IEnumerable<SeoSlot> Flatten(SeoSlot slot)
    {
        yield return slot;
        if (slot.Nested is null)
            yield break;
        foreach (var sub in slot.Nested.Slots.SelectMany(Flatten))
            yield return sub;
    }

    private void Validate(SeoCatalogueDocument document)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in document.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id) || !ids.Add(template.Id))
                throw new InvalidOperationException($"Catalogue template id '{template.Id}' is missing or duplicated.");

            var baseType = RequireType(template.SchemaType, $"template '{template.Id}'");
            foreach (var variant in template.Variants)
            {
                var variantType = RequireType(variant.SchemaType, $"variant of '{template.Id}'");
                if (!baseType.IsAssignableFrom(variantType) && !ImplementsInterfaceOf(variantType, template.SchemaType))
                    throw new InvalidOperationException($"Catalogue variant '{variant.SchemaType}' is not a subtype of '{template.SchemaType}' ('{template.Id}').");
            }

            foreach (var slot in template.Slots.SelectMany(Flatten))
            {
                if (string.IsNullOrWhiteSpace(slot.SchemaProperty))
                    throw new InvalidOperationException($"Template '{template.Id}' has a slot without a schemaProperty.");
                if (slot.Nested is not null)
                    RequireType(slot.Nested.SchemaType, $"slot '{slot.SchemaProperty}' of '{template.Id}'");
            }
        }

        foreach (var businessType in document.BusinessTypes)
            RequireType(businessType.SchemaType, "businessTypes");
    }

    private Type RequireType(string schemaType, string where) =>
        _registry.GetClrType(schemaType)
        ?? throw new InvalidOperationException($"Catalogue references unknown Schema.org type '{schemaType}' in {where}.");

    private static bool ImplementsInterfaceOf(Type candidate, string schemaType) =>
        candidate.GetInterfaces().Any(i => string.Equals(i.Name, "I" + schemaType, StringComparison.Ordinal));
}
