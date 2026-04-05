using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class PropertyValueResolverFactoryTests
{
    [Fact]
    public void GetResolver_NullAlias_ReturnsFallbackResolver()
    {
        var factory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);

        var resolver = factory.GetResolver(null);

        resolver.Should().BeOfType<DefaultPropertyValueResolver>();
    }

    [Fact]
    public void GetResolver_EmptyAlias_ReturnsFallbackResolver()
    {
        var factory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);

        var resolver = factory.GetResolver(string.Empty);

        resolver.Should().BeOfType<DefaultPropertyValueResolver>();
    }

    [Fact]
    public void GetResolver_UnknownAlias_ReturnsFallbackResolver()
    {
        var factory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);

        var resolver = factory.GetResolver("Umbraco.UnknownEditor");

        resolver.Should().BeOfType<DefaultPropertyValueResolver>();
    }

    [Fact]
    public void GetResolver_MediaPickerAlias_ReturnsMediaPickerResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance)
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.MediaPicker3");

        resolver.Should().BeOfType<MediaPickerResolver>();
    }

    [Fact]
    public void GetResolver_RichTextAlias_ReturnsRichTextResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new RichTextResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.RichText");

        resolver.Should().BeOfType<RichTextResolver>();
    }

    [Fact]
    public void GetResolver_TinyMceAlias_ReturnsRichTextResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new RichTextResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.TinyMCE");

        resolver.Should().BeOfType<RichTextResolver>();
    }

    [Fact]
    public void GetResolver_MarkdownAlias_ReturnsRichTextResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new RichTextResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.MarkdownEditor");

        resolver.Should().BeOfType<RichTextResolver>();
    }

    [Fact]
    public void GetResolver_ContentPickerAlias_ReturnsContentPickerResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new ContentPickerResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.ContentPicker");

        resolver.Should().BeOfType<ContentPickerResolver>();
    }

    [Fact]
    public void GetResolver_BlockListAlias_ReturnsBlockContentResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BlockContentResolver(NullLogger<BlockContentResolver>.Instance)
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.BlockList");

        resolver.Should().BeOfType<BlockContentResolver>();
    }

    [Fact]
    public void GetResolver_BlockGridAlias_ReturnsBlockContentResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BlockContentResolver(NullLogger<BlockContentResolver>.Instance)
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.BlockGrid");

        resolver.Should().BeOfType<BlockContentResolver>();
    }

    [Fact]
    public void GetResolver_LegacyMediaPickerAlias_ReturnsMediaPickerResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance)
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.MediaPicker");

        resolver.Should().BeOfType<MediaPickerResolver>();
    }

    [Fact]
    public void GetResolver_CaseInsensitive_ReturnsCorrectResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance)
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("umbraco.mediapicker3");

        resolver.Should().BeOfType<MediaPickerResolver>();
    }

    [Fact]
    public void GetResolver_HigherPriorityWins_WhenMultipleResolversForSameAlias()
    {
        var lowPriority = new TestResolverLow();
        var highPriority = new TestResolverHigh();
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            lowPriority,
            highPriority
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.TestEditor");

        resolver.Should().BeSameAs(highPriority);
    }

    [Fact]
    public void GetResolver_NoRegisteredResolvers_CreatesDefaultFallback()
    {
        var factory = new PropertyValueResolverFactory([]);

        var resolver = factory.GetResolver("Umbraco.Anything");

        resolver.Should().BeOfType<DefaultPropertyValueResolver>();
    }

    [Fact]
    public void GetResolver_MultiUrlPickerAlias_ReturnsMultiUrlPickerResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new MultiUrlPickerResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.MultiUrlPicker");

        resolver.Should().BeOfType<MultiUrlPickerResolver>();
    }

    [Fact]
    public void GetResolver_TrueFalseAlias_ReturnsBooleanResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BooleanResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.TrueFalse");

        resolver.Should().BeOfType<BooleanResolver>();
    }

    [Fact]
    public void GetResolver_IntegerAlias_ReturnsNumericResolver()
    {
        var resolvers = new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new NumericResolver()
        };
        var factory = new PropertyValueResolverFactory(resolvers);

        var resolver = factory.GetResolver("Umbraco.Integer");

        resolver.Should().BeOfType<NumericResolver>();
    }

    private class TestResolverLow : IPropertyValueResolver
    {
        public IEnumerable<string> SupportedEditorAliases => ["Umbraco.TestEditor"];
        public int Priority => 5;
        public object? Resolve(PropertyResolverContext context) => null;
    }

    private class TestResolverHigh : IPropertyValueResolver
    {
        public IEnumerable<string> SupportedEditorAliases => ["Umbraco.TestEditor"];
        public int Priority => 20;
        public object? Resolve(PropertyResolverContext context) => null;
    }
}
