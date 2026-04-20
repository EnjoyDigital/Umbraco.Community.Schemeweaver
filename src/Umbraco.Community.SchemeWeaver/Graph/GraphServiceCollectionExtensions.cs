using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// DI helpers for registering custom <see cref="IGraphPiece"/> implementations.
/// Built-in pieces are registered by <c>SchemeWeaverComposer</c>; consumers
/// who want to add their own piece (e.g. a site-specific <c>ProductPiece</c>)
/// call <see cref="AddSchemeWeaverGraphPiece{TPiece}"/> from their own composer.
/// </summary>
public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// Register a graph piece. Pieces are resolved as a collection by
    /// <see cref="GraphGenerator"/> — each registration adds another piece
    /// to the emitted @graph. Piece instances are scoped per request to
    /// mirror the rest of the SchemeWeaver service lifetimes.
    /// </summary>
    public static IServiceCollection AddSchemeWeaverGraphPiece<TPiece>(this IServiceCollection services)
        where TPiece : class, IGraphPiece
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IGraphPiece, TPiece>());
        return services;
    }
}
