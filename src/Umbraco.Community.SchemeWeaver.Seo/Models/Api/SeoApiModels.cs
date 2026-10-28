using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Templates;

namespace Umbraco.Community.SchemeWeaver.Seo.Models.Api;

/// <summary><c>GET templates</c>.</summary>
public sealed class SeoTemplateCatalogueDto
{
    public IReadOnlyList<SeoTemplate> Templates { get; init; } = [];
    public IReadOnlyList<SeoRetiredFeature> Retired { get; init; } = [];
    public IReadOnlyList<SeoBusinessType> BusinessTypes { get; init; } = [];
}

/// <summary><c>GET server-context</c>: what the section needs to know about this install.</summary>
public sealed class SeoServerContextDto
{
    public string SectionAlias { get; init; } = SchemeWeaverSeoConstants.SectionAlias;
    public string? SiteUrl { get; init; }
    public bool SiteIdentityConfigured { get; init; }
    public bool HideDeveloperJsonLdTab { get; init; }

    /// <summary>True when the uSync satellite is installed (mappings may also live on disk).</summary>
    public bool MappingsManagedAsCode { get; init; }

    /// <summary><c>Off</c>, <c>Seed</c> or <c>Upsert</c>; <c>Upsert</c> overwrites backoffice edits on restart.</summary>
    public string USyncBootImport { get; init; } = "Off";
    public bool ExportMappingsToUSyncOnSave { get; init; }
    public bool UseGraphModel { get; init; }
    public bool EmitBreadcrumbsInDeliveryApi { get; init; }
    public string? PublicSiteUrl { get; init; }
    public bool HasPublishedContent { get; init; }
}

/// <summary>One row of the Content types view.</summary>
public sealed class SeoContentTypeStatusDto
{
    public string Alias { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid Key { get; init; }
    public string? Icon { get; init; }
    public bool IsElement { get; init; }
    public bool HasTemplate { get; init; }
    public bool AllowedAsRoot { get; init; }
    public int PublishedCount { get; init; }

    /// <summary><c>not-set-up</c>, <c>needs-attention</c>, <c>set-up</c> or <c>developer-managed</c>.</summary>
    public string Status { get; init; } = SchemeWeaverSeoConstants.ContentTypeStatus.NotSetUp;
    public SeoMappingSummaryDto? Mapping { get; init; }
}

public sealed class SeoMappingSummaryDto
{
    public string SchemaTypeName { get; init; } = string.Empty;
    public string? TemplateId { get; init; }
    public string? TemplateLabel { get; init; }
    public bool IsRetired { get; init; }
    public bool IsEnabled { get; init; }
    public int RequiredFilled { get; init; }
    public int RequiredTotal { get; init; }
    public int RecommendedFilled { get; init; }
    public int RecommendedTotal { get; init; }
    public int AdvancedRowCount { get; init; }
    public IReadOnlyList<string> MissingRequired { get; init; } = [];
}

/// <summary>How one slot is (or will be) filled. Kinds per <see cref="SchemeWeaverSeoConstants.SlotValueKinds"/>.</summary>
public sealed class SeoSlotValueDto
{
    public string Kind { get; set; } = SchemeWeaverSeoConstants.SlotValueKinds.None;
    public string? PropertyAlias { get; set; }
    public string? EditorAlias { get; set; }
    public string? FixedValue { get; set; }
    public string? TargetPieceKey { get; set; }
    public int? Confidence { get; set; }
    public bool IsAutoMapped { get; set; }

    /// <summary>For <c>nested</c>: sub-slot values keyed by schema property (camelCase).</summary>
    public Dictionary<string, SeoSlotValueDto> Nested { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>For <c>blocks</c> / <c>advanced</c>: a one-line description of the preserved row.</summary>
    public string? Summary { get; set; }
}

public sealed class SeoSlotStateDto
{
    public SeoSlot Slot { get; init; } = new();
    public SeoSlotValueDto Value { get; init; } = new();
}

/// <summary>A mapping seen through a template: slots plus the rows the template does not know about.</summary>
public sealed class SeoProjectionDto
{
    public string TemplateId { get; init; } = string.Empty;
    public string SchemaTypeName { get; init; } = string.Empty;
    public bool HasExistingMapping { get; init; }
    public List<SeoSlotStateDto> Slots { get; init; } = [];

    /// <summary>Rows preserved verbatim; shown read-only as "managed by developers".</summary>
    public List<PropertyMappingDto> AdvancedRows { get; init; } = [];

    /// <summary>Auto-map suggestions for properties no slot covers (informational).</summary>
    public List<PropertyMappingSuggestion> UnslottedSuggestions { get; init; } = [];
}

public sealed class SeoSetupRequest
{
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Default type or one of the template's variants. Null = the template's default.</summary>
    public string? SchemaTypeName { get; set; }
}

public sealed class SeoMappingSaveRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string? SchemaTypeName { get; set; }

    /// <summary>Slot values keyed by schema property. Slots omitted are treated as <c>none</c>.</summary>
    public Dictionary<string, SeoSlotValueDto> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SeoSamplePageDto
{
    public Guid Key { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Url { get; init; }
    public DateTime UpdateDate { get; init; }
}

public sealed class SeoPageOverrideDto
{
    public Guid ContentKey { get; init; }
    public string? SchemaTypeName { get; init; }
    public bool IsExcluded { get; init; }
    public DateTime? UpdatedDate { get; init; }
}

public sealed class SeoPageOverrideRequest
{
    public string? SchemaTypeName { get; set; }
    public bool IsExcluded { get; set; }
}

/// <summary>Everything the content item's Schema tab shows.</summary>
public sealed class SeoPageSummaryDto
{
    public Guid ContentKey { get; init; }
    public string ContentTypeAlias { get; init; } = string.Empty;
    public string ContentTypeName { get; init; } = string.Empty;
    public bool HasMapping { get; init; }
    public string? TemplateId { get; init; }
    public string? TemplateLabel { get; init; }

    /// <summary>The type the mapping emits by default.</summary>
    public string? DefaultSchemaTypeName { get; init; }

    /// <summary>The type this page emits once its override is applied.</summary>
    public string? EffectiveSchemaTypeName { get; init; }
    public IReadOnlyList<SeoTemplateVariant> AllowedPageTypes { get; init; } = [];
    public SeoPageOverrideDto? Override { get; init; }
    public bool IsExcluded { get; init; }
    public bool IsPublished { get; init; }
    public string? PublicUrl { get; init; }
    public JsonLdPreviewResponse? Preview { get; init; }
}

public sealed class SeoHealthReportDto
{
    public DateTime GeneratedAt { get; init; }
    public bool Cached { get; init; }
    public int ContentTypesChecked { get; init; }
    public int Critical { get; init; }
    public int Warning { get; init; }
    public List<SeoHealthItemDto> Items { get; init; } = [];
}

public sealed class SeoHealthItemDto
{
    public string Alias { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SchemaTypeName { get; init; } = string.Empty;
    public string? TemplateId { get; init; }
    public Guid? SamplePageKey { get; init; }
    public string? SamplePageName { get; init; }
    public int Critical { get; init; }
    public int Warning { get; init; }
    public List<ValidationIssueDto> Issues { get; init; } = [];

    /// <summary><c>ok</c>, <c>needs-attention</c> or <c>no-published-page</c>.</summary>
    public string Status { get; init; } = "ok";
}
