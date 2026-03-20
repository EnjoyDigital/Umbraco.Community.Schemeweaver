namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Collects all registered <see cref="IPropertyValueResolver"/> implementations from DI
/// and selects the appropriate one based on property editor alias.
/// </summary>
public class PropertyValueResolverFactory : IPropertyValueResolverFactory
{
    private readonly Dictionary<string, IPropertyValueResolver> _resolversByAlias;
    private readonly IPropertyValueResolver _defaultResolver;

    public PropertyValueResolverFactory(IEnumerable<IPropertyValueResolver> resolvers)
    {
        _resolversByAlias = new Dictionary<string, IPropertyValueResolver>(StringComparer.OrdinalIgnoreCase);
        IPropertyValueResolver? fallback = null;

        // Sort by priority descending so highest-priority wins for each alias
        foreach (var resolver in resolvers.OrderByDescending(r => r.Priority))
        {
            var aliases = resolver.SupportedEditorAliases.ToList();
            if (aliases.Count == 0)
            {
                // First fallback by priority wins
                fallback ??= resolver;
                continue;
            }

            foreach (var alias in aliases)
            {
                _resolversByAlias.TryAdd(alias, resolver);
            }
        }

        _defaultResolver = fallback ?? new DefaultPropertyValueResolver();
    }

    public IPropertyValueResolver GetResolver(string? editorAlias)
    {
        if (string.IsNullOrEmpty(editorAlias))
            return _defaultResolver;

        return _resolversByAlias.TryGetValue(editorAlias, out var resolver)
            ? resolver
            : _defaultResolver;
    }
}
