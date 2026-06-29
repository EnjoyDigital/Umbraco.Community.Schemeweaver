#if !UMBRACO18
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AISchemaMapper"/>.
///
/// These tests are 17-only because <see cref="AISchemaMapper"/> depends on
/// <c>Umbraco.AI.Core</c>, which has no Umbraco 18 build.
/// </summary>
public class AISchemaMapperTests
{
    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    private readonly IAIChatService _chatService = Substitute.For<IAIChatService>();
    private readonly IContentTypeService _aiMapperContentTypeService = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _schemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>();
    private readonly ILogger<AISchemaMapper> _logger = Substitute.For<ILogger<AISchemaMapper>>();

    // The heuristic mapper takes the concrete SchemaAutoMapper (not the interface),
    // so we construct a real instance with its own mocked service dependencies.
    private readonly IContentTypeService _heuristicContentTypeService = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _heuristicSchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>();

    private SchemaAutoMapper CreateHeuristicMapper() =>
        new(_heuristicContentTypeService, _heuristicSchemaTypeRegistry);

    private AISchemaMapper CreateMapper() =>
        new(_chatService, _aiMapperContentTypeService, _schemaTypeRegistry,
            CreateHeuristicMapper(), _logger);

    // -----------------------------------------------------------------------
    // ExtractJson tests — pure parsing, no mocks needed
    // -----------------------------------------------------------------------

    [Fact]
    public void ExtractJson_PlainArray_ReturnsSame()
    {
        var input = """[{"schemaTypeName": "Article", "confidence": 90}]""";
        AISchemaMapper.ExtractJson(input).Should().Be(input);
    }

    [Fact]
    public void ExtractJson_MarkdownFenced_StripsDelimiters()
    {
        var input = """
            ```json
            [{"schemaTypeName": "Article"}]
            ```
            """;
        var result = AISchemaMapper.ExtractJson(input);
        result.Should().Contain("[");
        result.Should().Contain("Article");
        result.Should().NotContain("```");
    }

    [Fact]
    public void ExtractJson_ExtraTextAround_ExtractsArray()
    {
        var input = """Here is the result: [{"schemaTypeName": "Article"}] Hope this helps!""";
        var result = AISchemaMapper.ExtractJson(input);
        result.Should().StartWith("[");
        result.Should().EndWith("]");
    }

    // -----------------------------------------------------------------------
    // SuggestSchemaTypesAsync tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SuggestSchemaTypesAsync_ContentTypeNotFound_ReturnsEmpty()
    {
        _aiMapperContentTypeService.Get("nonexistent").Returns((IContentType?)null);

        var mapper = CreateMapper();
        var result = await mapper.SuggestSchemaTypesAsync("nonexistent");

        result.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // SuggestPropertyMappingsAsync tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SuggestPropertyMappingsAsync_ContentTypeNotFound_ReturnsHeuristicFallback()
    {
        // Both the AI mapper and the heuristic mapper see null for this alias.
        // The heuristic mapper returns an empty list (SchemaAutoMapper returns empty
        // when the content type is not found) — and so does the AI mapper, because
        // it returns heuristicSuggestions immediately when contentType is null.
        _aiMapperContentTypeService.Get("nonexistent").Returns((IContentType?)null);
        _heuristicContentTypeService.Get("nonexistent").Returns((IContentType?)null);
        _heuristicSchemaTypeRegistry.GetProperties("Article")
            .Returns(Array.Empty<SchemaPropertyInfo>());

        var mapper = CreateMapper();
        var result = await mapper.SuggestPropertyMappingsAsync("nonexistent", "Article");

        // With no content type the heuristic also returns empty → the combined result is empty.
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SuggestPropertyMappingsAsync_ContentTypeFound_HeuristicRunsFirst()
    {
        // Set up a content type that IS found so heuristic produces results.
        var heuristicContentType = Substitute.For<IContentType>();
        heuristicContentType.Alias.Returns("blogPost");
        heuristicContentType.Name.Returns("Blog Post");
        var headlineProp = Substitute.For<IPropertyType>();
        headlineProp.Alias.Returns("headline");
        headlineProp.PropertyEditorAlias.Returns("Umbraco.TextBox");
        heuristicContentType.PropertyTypes.Returns(new[] { headlineProp });
        heuristicContentType.CompositionPropertyTypes.Returns(new[] { headlineProp });
        _heuristicContentTypeService.Get("blogPost").Returns(heuristicContentType);

        _heuristicSchemaTypeRegistry.GetProperties("BlogPosting").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
        });

        // AI mapper also needs the content type — but we'll make the AI call fail
        // so the heuristic fallback is exercised.
        var aiContentType = Substitute.For<IContentType>();
        aiContentType.Alias.Returns("blogPost");
        aiContentType.Name.Returns("Blog Post");
        aiContentType.PropertyTypes.Returns(new[] { headlineProp });
        aiContentType.CompositionPropertyTypes.Returns(new[] { headlineProp });
        _aiMapperContentTypeService.Get("blogPost").Returns(aiContentType);

        _schemaTypeRegistry.GetProperties("BlogPosting")
            .Returns(Array.Empty<SchemaPropertyInfo>());

        // Make AI throw so heuristic fallback is returned.
        _chatService.GetChatResponseAsync(
            Arg.Any<Action<AIChatBuilder>>(),
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new Exception("AI service unavailable"));

        var mapper = CreateMapper();
        var result = await mapper.SuggestPropertyMappingsAsync("blogPost", "BlogPosting");

        // Heuristic found "headline" → exact match confidence 100.
        result.Should().Contain(s => s.SchemaPropertyName == "headline");
        var headlineResult = result.Single(s => s.SchemaPropertyName == "headline");
        headlineResult.Confidence.Should().Be(100);
    }

    [Fact]
    public async Task SuggestPropertyMappingsAsync_AIFails_ReturnsHeuristicFallback()
    {
        var contentType = Substitute.For<IContentType>();
        contentType.Alias.Returns("blogPost");
        contentType.Name.Returns("Blog Post");
        var pt = Substitute.For<IPropertyType>();
        pt.Alias.Returns("headline");
        pt.PropertyEditorAlias.Returns("Umbraco.TextBox");
        contentType.PropertyTypes.Returns(new[] { pt });
        contentType.CompositionPropertyTypes.Returns(new[] { pt });

        _heuristicContentTypeService.Get("blogPost").Returns(contentType);
        _heuristicSchemaTypeRegistry.GetProperties("BlogPosting").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
        });

        _aiMapperContentTypeService.Get("blogPost").Returns(contentType);
        _schemaTypeRegistry.GetProperties("BlogPosting")
            .Returns(Array.Empty<SchemaPropertyInfo>());

        // AI throws an exception
        _chatService.GetChatResponseAsync(
            Arg.Any<Action<AIChatBuilder>>(),
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new Exception("AI service unavailable"));

        var mapper = CreateMapper();
        var result = await mapper.SuggestPropertyMappingsAsync("blogPost", "BlogPosting");

        result.Should().Contain(s => s.SchemaPropertyName == "headline");
    }
}
#endif
