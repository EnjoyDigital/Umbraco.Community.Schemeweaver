using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TestSupport;

/// <summary>
/// Installs a hermetic <see cref="StaticServiceProvider"/> for tests that drive code through
/// Umbraco's FriendlyPublishedContentExtensions (e.g. <c>media.Value&lt;int?&gt;("umbracoWidth")</c>
/// inside <c>MediaImageObjectFactory</c>). The Umbraco 17 type initializer eagerly resolves ~17
/// services from <see cref="StaticServiceProvider"/> (18 resolves lazily per call); a fixed seed
/// list is a moving target across minors, so this provider hands out NSubstitute mocks on demand,
/// with a real <see cref="NoopPublishedValueFallback"/> so <c>Value&lt;T&gt;()</c> behaves.
/// Mirrors the guard MediaPickerResolverTests carries privately — call
/// <see cref="EnsureInstalled"/> before any media-resolving test body. Only installs when no
/// ambient (integration-host) provider can already satisfy the friendly extensions, because
/// xUnit class scheduling is nondeterministic and these tests must stay hermetic.
/// </summary>
internal static class HermeticStaticServiceProvider
{
    private static readonly object _gate = new();

    public static void EnsureInstalled()
    {
        lock (_gate)
        {
            if (StaticServiceProvider.Instance?.GetService<IPublishedValueFallback>() is null)
            {
                StaticServiceProvider.Instance = new SubstituteServiceProvider();
            }
        }
    }

    /// <summary>
    /// Resolves a fresh NSubstitute mock for any interface requested, so eager static service
    /// resolution in Umbraco's friendly extensions can never poison a type initializer.
    /// </summary>
    private sealed class SubstituteServiceProvider : IServiceProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object?> _services = new();

        public object? GetService(Type serviceType)
            => _services.GetOrAdd(serviceType, static t =>
                t == typeof(IPublishedValueFallback) ? new NoopPublishedValueFallback()
                : t.IsInterface ? Substitute.For(new[] { t }, Array.Empty<object>())
                : null);
    }
}
