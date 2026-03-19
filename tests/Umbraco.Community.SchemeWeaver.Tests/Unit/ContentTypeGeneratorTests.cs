using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class ContentTypeGeneratorTests
{
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly IDataTypeService _dataTypeService = Substitute.For<IDataTypeService>();
    private readonly ISchemaTypeRegistry _registry = Substitute.For<ISchemaTypeRegistry>();
    private readonly IShortStringHelper _shortStringHelper = Substitute.For<IShortStringHelper>();
    private readonly ILogger<ContentTypeGenerator> _logger = Substitute.For<ILogger<ContentTypeGenerator>>();
    private readonly ContentTypeGenerator _sut;

    public ContentTypeGeneratorTests()
    {
        _sut = new ContentTypeGenerator(_contentTypeService, _dataTypeService, _registry, _shortStringHelper, _logger);
    }

    private void SetupSchemaType(string schemaTypeName, params (string Name, string PropertyType)[] properties)
    {
        _registry.GetType(schemaTypeName).Returns(new SchemaTypeInfo { Name = schemaTypeName });
        _registry.GetProperties(schemaTypeName).Returns(
            properties.Select(p => new SchemaPropertyInfo { Name = p.Name, PropertyType = p.PropertyType }));
    }

    private void SetupDataType(string editorAlias)
    {
        var dataType = Substitute.For<IDataType>();
        dataType.EditorAlias.Returns(editorAlias);
        _dataTypeService.GetByEditorAlias(editorAlias).Returns(new[] { dataType });
    }

    [Fact]
    public void GenerateContentType_TextProperty_MapsToTextBox()
    {
        SetupSchemaType("Article", ("Headline", "Text"));
        SetupDataType(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox);
        _contentTypeService.Get("article").Returns((IContentType?)null);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Article",
            DocumentTypeName = "Article",
            DocumentTypeAlias = "article",
            SelectedProperties = new List<string> { "Headline" }
        };

        var result = _sut.GenerateContentType(request);

        result.Should().NotBeEmpty();
        _contentTypeService.Received(1).Save(Arg.Any<IContentType>());
    }

    [Fact]
    public void GenerateContentType_DateTimeProperty_MapsToDateTime()
    {
        SetupSchemaType("Article", ("DatePublished", "DateTime"));
        SetupDataType(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.DateTime);
        _contentTypeService.Get("article").Returns((IContentType?)null);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Article",
            DocumentTypeName = "Article",
            DocumentTypeAlias = "article",
            SelectedProperties = new List<string> { "DatePublished" }
        };

        var result = _sut.GenerateContentType(request);

        result.Should().NotBeEmpty();
        _contentTypeService.Received(1).Save(Arg.Any<IContentType>());
    }

    [Fact]
    public void GenerateContentType_BooleanProperty_MapsToTrueFalse()
    {
        SetupSchemaType("Article", ("IsAccessibleForFree", "Boolean"));
        SetupDataType(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.Boolean);
        _contentTypeService.Get("article").Returns((IContentType?)null);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Article",
            DocumentTypeName = "Article",
            DocumentTypeAlias = "article",
            SelectedProperties = new List<string> { "IsAccessibleForFree" }
        };

        var result = _sut.GenerateContentType(request);

        result.Should().NotBeEmpty();
    }

    [Fact]
    public void GenerateContentType_URLProperty_MapsToTextBox()
    {
        SetupSchemaType("Article", ("Url", "URL"));
        SetupDataType(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox);
        _contentTypeService.Get("article").Returns((IContentType?)null);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Article",
            DocumentTypeName = "Article",
            DocumentTypeAlias = "article",
            SelectedProperties = new List<string> { "Url" }
        };

        var result = _sut.GenerateContentType(request);

        result.Should().NotBeEmpty();
    }

    [Fact]
    public void GenerateContentType_SchemaTypeNotFound_ThrowsArgumentException()
    {
        _registry.GetType("Unknown").Returns((SchemaTypeInfo?)null);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Unknown",
            DocumentTypeName = "Unknown",
            DocumentTypeAlias = "unknown"
        };

        var act = () => _sut.GenerateContentType(request);

        act.Should().Throw<ArgumentException>().WithMessage("*not found*");
    }

    [Fact]
    public void GenerateContentType_ExistingContentType_ThrowsInvalidOperationException()
    {
        SetupSchemaType("Article", ("Headline", "Text"));
        var existing = Substitute.For<IContentType>();
        _contentTypeService.Get("article").Returns(existing);

        var request = new ContentTypeGenerationRequest
        {
            SchemaTypeName = "Article",
            DocumentTypeName = "Article",
            DocumentTypeAlias = "article"
        };

        var act = () => _sut.GenerateContentType(request);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }
}
