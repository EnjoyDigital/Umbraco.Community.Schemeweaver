using Xunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class AISchemaMapperTests
{
    private readonly IAIChatService _chatService = Substitute.For<IAIChatService>();
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _schemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>();
    private readonly ISchemaAutoMapper _heuristicMapper = Substitute.For<ISchemaAutoMapper>();
    private readonly ILogger<AISchemaMapper> _logger = Substitute.For<ILogger<AISchemaMapper>>();

    private AISchemaMapper CreateMapper() =>
        new(_chatService, _contentTypeService, _schemaTypeRegistry, _heuristicMapper, _logger);

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

    [Fact]
    public async Task SuggestSchemaTypesAsync_ContentTypeNotFound_ReturnsEmpty()
    {
        _contentTypeService.Get("nonexistent").Returns((IContentType?)null);

        var mapper = CreateMapper();
        var result = await mapper.SuggestSchemaTypesAsync("nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SuggestPropertyMappingsAsync_ContentTypeNotFound_ReturnsHeuristicFallback()
    {
        _contentTypeService.Get("nonexistent").Returns((IContentType?)null);
        var heuristicResults = new[]
        {
            new PropertyMappingSuggestion { SchemaPropertyName = "name", Confidence = 80 }
        };
        _heuristicMapper.SuggestMappings("nonexistent", "Article").Returns(heuristicResults);

        var mapper = CreateMapper();
        var result = await mapper.SuggestPropertyMappingsAsync("nonexistent", "Article");

        result.Should().HaveCount(1);
        result[0].SchemaPropertyName.Should().Be("name");
    }

    [Fact]
    public async Task SuggestPropertyMappingsAsync_AIFails_ReturnsHeuristicFallback()
    {
        var contentType = Substitute.For<IContentType>();
        contentType.Alias.Returns("blogPost");
        contentType.Name.Returns("Blog Post");
        contentType.PropertyTypes.Returns(new PropertyTypeCollection(true));
        _contentTypeService.Get("blogPost").Returns(contentType);

        _schemaTypeRegistry.GetProperties("BlogPosting").Returns(Array.Empty<SchemaPropertyInfo>());

        var heuristicResults = new[]
        {
            new PropertyMappingSuggestion { SchemaPropertyName = "headline", Confidence = 80 }
        };
        _heuristicMapper.SuggestMappings("blogPost", "BlogPosting").Returns(heuristicResults);

        // AI throws an exception
        _chatService.GetChatResponseAsync(
            Arg.Any<Action<AIChatBuilder>>(),
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<CancellationToken>()
        ).Returns<ChatResponse>(x => throw new Exception("AI service unavailable"));

        var mapper = CreateMapper();
        var result = await mapper.SuggestPropertyMappingsAsync("blogPost", "BlogPosting");

        result.Should().Contain(s => s.SchemaPropertyName == "headline");
    }
}
