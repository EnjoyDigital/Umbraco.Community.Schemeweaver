namespace Umbraco.Community.SchemeWeaver.Seo.Templates;

/// <summary>The curated content-kind catalogue (singleton, loaded once from the embedded JSON).</summary>
public interface ISeoTemplateCatalogue
{
    /// <summary>Every template, retired ones included (the UI hides those behind a toggle).</summary>
    IReadOnlyList<SeoTemplate> All { get; }

    IReadOnlyList<SeoRetiredFeature> Retired { get; }

    /// <summary>Curated LocalBusiness subtypes offered for the local-business kind and the site identity.</summary>
    IReadOnlyList<SeoBusinessType> BusinessTypes { get; }

    SeoTemplate? Get(string id);

    /// <summary>
    /// The template that owns <paramref name="schemaType"/>: an exact match on the default type
    /// or a variant (or a curated business type for the local-business kind), else the nearest
    /// template whose default type is an ancestor of it. Null when no template fits (the mapping
    /// is then "developer-managed").
    /// </summary>
    SeoTemplate? FindForSchemaType(string schemaType);

    /// <summary>The Schema.org types a page of <paramref name="template"/> may be retyped to.</summary>
    IReadOnlyList<SeoTemplateVariant> AllowedPageTypes(SeoTemplate template);
}
