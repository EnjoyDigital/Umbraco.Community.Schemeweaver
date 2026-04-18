using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Caching, singleton implementation of <see cref="IJsonLdBlocksProvider"/>. Resolves the
/// scoped <see cref="IJsonLdGenerator"/> per generation via <see cref="IServiceScopeFactory"/>
/// — mirrors the pattern in <c>SchemaJsonLdContentIndexHandler</c> so the index-time and
/// request-time pipelines produce identical output.
/// </summary>
public sealed class JsonLdBlocksProvider : IJsonLdBlocksProvider, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<JsonLdBlocksProvider> _logger;

    // Per-content-key cancellation tokens drive eviction. Each cache entry is linked to its
    // content key's CTS; cancelling the CTS evicts every culture variant for that content in
    // one go. A global CTS covers "invalidate everything" (schema-mapping writes).
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _perContentTokens = new();
    private CancellationTokenSource _globalToken = new();

    public JsonLdBlocksProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IOptions<SchemeWeaverOptions> options,
        ILogger<JsonLdBlocksProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string[] GetBlocks(IPublishedContent content, string? culture)
    {
        ArgumentNullException.ThrowIfNull(content);

        var cacheKey = BuildCacheKey(content.Key, culture);
        return _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _options.CacheDuration;
            var perContent = _perContentTokens.GetOrAdd(content.Key, _ => new CancellationTokenSource());
            entry.AddExpirationToken(new CancellationChangeToken(perContent.Token));
            entry.AddExpirationToken(new CancellationChangeToken(_globalToken.Token));
            return Generate(content, culture);
        }) ?? Array.Empty<string>();
    }

    public void Invalidate(Guid contentKey)
    {
        if (_perContentTokens.TryRemove(contentKey, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* already evicted */ }
            cts.Dispose();
        }
    }

    public void InvalidateAll()
    {
        var previous = Interlocked.Exchange(ref _globalToken, new CancellationTokenSource());
        try { previous.Cancel(); }
        catch (ObjectDisposedException) { /* already evicted */ }
        previous.Dispose();

        // Drop every per-content token too — otherwise a future Invalidate(key) would no-op
        // until a cache miss rebuilt the CTS entry.
        foreach (var key in _perContentTokens.Keys.ToArray())
        {
            Invalidate(key);
        }
    }

    public void Dispose()
    {
        foreach (var cts in _perContentTokens.Values)
        {
            try { cts.Dispose(); } catch { /* best effort */ }
        }
        _perContentTokens.Clear();
        try { _globalToken.Dispose(); } catch { /* best effort */ }
    }

    internal static string BuildCacheKey(Guid contentKey, string? culture)
        => $"schemeweaver:jsonld:{contentKey:N}:{culture?.ToLowerInvariant() ?? "none"}";

    private string[] Generate(IPublishedContent content, string? culture)
    {
        var all = new List<string>();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IJsonLdGenerator>();

            all.AddRange(generator.GenerateInheritedJsonLdStrings(content, culture));

            if (_options.EmitBreadcrumbsInDeliveryApi)
            {
                var breadcrumb = generator.GenerateBreadcrumbJsonLd(content, culture);
                if (!string.IsNullOrEmpty(breadcrumb)) all.Add(breadcrumb);
            }

            var main = generator.GenerateJsonLdString(content, culture);
            if (!string.IsNullOrEmpty(main)) all.Add(main);

            all.AddRange(generator.GenerateBlockElementJsonLdStrings(content, culture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentKey}", content.Key);
            return Array.Empty<string>();
        }

        return all.ToArray();
    }
}
