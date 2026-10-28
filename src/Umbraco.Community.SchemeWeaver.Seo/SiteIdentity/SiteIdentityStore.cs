using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;

/// <summary>
/// Key-value-store backed identity: one JSON document under
/// <see cref="SchemeWeaverSeoConstants.KeyValueKeys.SiteIdentity"/>, snapshot-cached so the graph
/// hot path never touches the database, and every write flushes every cached JSON-LD block
/// (site-level pieces are baked into each page's graph).
/// </summary>
public sealed class SiteIdentityStore : ISiteIdentityStore
{
    private const string CacheKey = "schemeweaver:seo:site-identity";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IKeyValueService _keyValueService;
    private readonly IMemoryCache _cache;
    private readonly IJsonLdBlocksProvider _blocksProvider;
    private readonly ISchemaTypeRegistry _registry;
    private readonly ILogger<SiteIdentityStore> _logger;

    public SiteIdentityStore(
        IKeyValueService keyValueService,
        IMemoryCache cache,
        IJsonLdBlocksProvider blocksProvider,
        ISchemaTypeRegistry registry,
        ILogger<SiteIdentityStore> logger)
    {
        _keyValueService = keyValueService;
        _cache = cache;
        _blocksProvider = blocksProvider;
        _registry = registry;
        _logger = logger;
    }

    public SiteIdentityDto? Get()
    {
        var holder = _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return new Holder(Load());
        });
        return holder?.Value is { } value ? Clone(value) : null;
    }

    public IReadOnlyList<string> Save(SiteIdentityDto identity, Guid? userKey)
    {
        var errors = Validate(identity);
        if (errors.Count > 0)
            return errors;

        identity.Name = identity.Name.Trim();
        identity.SameAs = identity.SameAs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        identity.UpdatedDate = DateTime.UtcNow;
        identity.UpdatedByUserKey = userKey;
        identity.IsConfigured = true;

        _keyValueService.SetValue(SchemeWeaverSeoConstants.KeyValueKeys.SiteIdentity, JsonSerializer.Serialize(identity, JsonOptions));
        _cache.Remove(CacheKey);
        _blocksProvider.InvalidateAll();
        _logger.LogInformation("SchemeWeaver SEO site identity saved ({Kind} {Name})", identity.Kind, identity.Name);
        return errors;
    }

    public void Delete()
    {
        // IKeyValueService has no delete; an empty value reads back as "not configured".
        _keyValueService.SetValue(SchemeWeaverSeoConstants.KeyValueKeys.SiteIdentity, string.Empty);
        _cache.Remove(CacheKey);
        _blocksProvider.InvalidateAll();
    }

    internal IReadOnlyList<string> Validate(SiteIdentityDto identity)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(identity.Name))
            errors.Add("A name is required.");

        if (identity.Kind is not ("organisation" or "person"))
            errors.Add("Kind must be 'organisation' or 'person'.");

        if (!string.IsNullOrWhiteSpace(identity.Url) && !Uri.TryCreate(identity.Url, UriKind.Absolute, out _))
            errors.Add("The site URL must be an absolute URL.");

        foreach (var sameAs in identity.SameAs.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!Uri.TryCreate(sameAs.Trim(), UriKind.Absolute, out _))
                errors.Add($"'{sameAs}' is not a valid profile URL.");
        }

        if (!string.IsNullOrWhiteSpace(identity.BusinessType))
        {
            var clr = _registry.GetClrType(identity.BusinessType);
            if (clr is null || !typeof(IOrganization).IsAssignableFrom(clr))
                errors.Add($"'{identity.BusinessType}' is not a recognised business type.");
        }

        if (!string.IsNullOrWhiteSpace(identity.SearchUrlTemplate))
        {
            var inputName = string.IsNullOrWhiteSpace(identity.SearchQueryInputName) ? "search_term_string" : identity.SearchQueryInputName;
            if (!identity.SearchUrlTemplate.Contains($"{{{inputName}}}", StringComparison.Ordinal))
                errors.Add($"The search URL must contain the {{{inputName}}} placeholder.");
        }

        return errors;
    }

    private SiteIdentityDto? Load()
    {
        try
        {
            var json = _keyValueService.GetValue(SchemeWeaverSeoConstants.KeyValueKeys.SiteIdentity);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var dto = JsonSerializer.Deserialize<SiteIdentityDto>(json, JsonOptions);
            if (dto is null)
                return null;

            dto.IsConfigured = !string.IsNullOrWhiteSpace(dto.Name);
            return dto;
        }
        catch (Exception ex)
        {
            // Never let a corrupt setting break the graph or the backoffice: behave as unconfigured.
            _logger.LogWarning(ex, "SchemeWeaver SEO site identity could not be read; treating as not configured");
            return null;
        }
    }

    private static SiteIdentityDto Clone(SiteIdentityDto value) =>
        JsonSerializer.Deserialize<SiteIdentityDto>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    private sealed record Holder(SiteIdentityDto? Value);
}
