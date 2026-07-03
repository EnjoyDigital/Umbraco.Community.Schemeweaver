using FluentAssertions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Drives the range validator against the REAL <see cref="SchemaTypeRegistry"/>
/// (no mocks) so the Schema.NET interface DAG — the thing the runtime actually
/// tests against — is exercised faithfully.
/// </summary>
public class SchemaRangeValidatorTests
{
    private readonly SchemaRangeValidator _sut;

    public SchemaRangeValidatorTests()
    {
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        _sut = new SchemaRangeValidator(registry, new SchemaRangeChecker(registry));
    }

    private static SchemaMappingDto Article(params PropertyMappingDto[] props)
        => new()
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = props.ToList()
        };

    [Fact]
    public void NestedPersonUnderHasPart_WarnsOnce_NamingPropertyTypeRangeAndAlternative()
    {
        // Article.HasPart range is CreativeWork; Person is not a CreativeWork.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "complexType",
            NestedSchemaTypeName = "Person"
        });

        var issues = _sut.Validate(dto);

        issues.Should().ContainSingle();
        var issue = issues[0];
        issue.Severity.Should().Be(ValidationSeverity.Warning);
        issue.Path.Should().Be("HasPart");
        issue.Message.Should().Contain("HasPart");
        issue.Message.Should().Contain("Person");
        issue.Message.Should().Contain("CreativeWork"); // accepted range named
        issue.Message.Should().Contain("About");         // deterministic alternative
    }

    [Fact]
    public void NestedArticleSubtypeUnderHasPart_NoWarning()
    {
        // Article IS a CreativeWork — a legitimate subtype, must not warn.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "complexType",
            NestedSchemaTypeName = "Article"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Publisher")]
    [InlineData("Author")]
    public void LocalBusinessUnderOrganizationRangedProperty_NoWarning(string schemaProperty)
    {
        // DA-FIX 1a: LocalBusiness : Place, ILocalBusiness : IPlace, IOrganization.
        // Concrete-only IsAssignableFrom(Organization) is FALSE (its base is Place),
        // but the interface DAG admits it under an Organization|Person range.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = schemaProperty,
            SourceType = "complexType",
            NestedSchemaTypeName = "LocalBusiness"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Restaurant")]
    [InlineData("Store")]
    public void BusinessSubtypeUnderAuthor_NoWarning(string nestedType)
    {
        // Restaurant/Store both implement IOrganization transitively.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Author",
            SourceType = "complexType",
            NestedSchemaTypeName = nestedType
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void BlockRoutes_WarnPerOffendingRoute_KeyedByBlockAlias()
    {
        // DA-FIX 1b: blockContent stores chosen types in ResolverConfig.routes[],
        // not NestedSchemaTypeName. One route is in range, one is not.
        var resolverConfig =
            """{"routes":[{"blockAlias":"personBlock","nestedSchemaType":"Person"},{"blockAlias":"articleBlock","nestedSchemaType":"Article"}]}""";
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "blockContent",
            ContentTypePropertyAlias = "blocks",
            ResolverConfig = resolverConfig
        });

        var issues = _sut.Validate(dto);

        issues.Should().ContainSingle("only the Person route is out of HasPart's CreativeWork range");
        issues[0].Path.Should().Be("HasPart");
        issues[0].Message.Should().Contain("personBlock");
        issues[0].Message.Should().Contain("Person");
    }

    [Fact]
    public void ScalarPropertyAutoWrapped_NoNestedType_NoWarning()
    {
        // DA-FIX 1c regression: textbox -> author is a scalar mapping with no
        // NestedSchemaTypeName; SchemaPropertySetter auto-wraps it at runtime.
        // Nothing object-typed to range-check here.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Author",
            SourceType = "property",
            ContentTypePropertyAlias = "authorName"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void PlainScalarProperty_NoWarning()
    {
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Name",
            SourceType = "property",
            ContentTypePropertyAlias = "title"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void PersonUnderAuthor_NoWarning()
    {
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Author",
            SourceType = "complexType",
            NestedSchemaTypeName = "Person"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void UnknownChosenType_NoWarning()
    {
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "complexType",
            NestedSchemaTypeName = "Persn" // typo
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void ObjectUnderScalarOnlyProperty_Warns()
    {
        // Article.Name accepts String only; a Thing there will be dropped.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Name",
            SourceType = "complexType",
            NestedSchemaTypeName = "Person"
        });

        var issues = _sut.Validate(dto);

        issues.Should().ContainSingle();
        issues[0].Path.Should().Be("Name");
    }

    [Fact]
    public void ReferenceSourceType_NoWarning_Deferred()
    {
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Author",
            SourceType = "reference",
            NestedSchemaTypeName = "Person",
            TargetPieceKey = "organization"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    // --- Inner complexTypeMappings blind spot (media logo trap) --------------

    /// <summary>
    /// Builds the validator against its RICHEST available constructor, injecting the real
    /// registry/checker and the supplied <see cref="IContentTypeService"/> (any other new
    /// dependency gets an NSubstitute mock). Inspecting the INNER complexTypeMappings needs an
    /// editor-alias lookup, so the fix is expected to add an IContentTypeService parameter —
    /// constructing reflectively keeps this file compiling against both the current 2-param
    /// ctor (the test then runs RED against current behaviour) and the fixed ctor.
    /// </summary>
    private static SchemaRangeValidator CreateValidator(IContentTypeService contentTypeService)
    {
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        var checker = new SchemaRangeChecker(registry);

        var ctor = typeof(SchemaRangeValidator).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var args = ctor.GetParameters()
            .Select(p =>
                p.ParameterType.IsInstanceOfType(registry) ? (object)registry
                : p.ParameterType.IsInstanceOfType(checker) ? checker
                : p.ParameterType.IsInstanceOfType(contentTypeService) ? contentTypeService
                : Substitute.For(new[] { p.ParameterType }, Array.Empty<object>()))
            .ToArray();

        return (SchemaRangeValidator)ctor.Invoke(args);
    }

    private static IContentTypeService ContentTypeServiceWith(
        string contentTypeAlias, params (string alias, string editorAlias)[] properties)
    {
        var contentType = Substitute.For<IContentType>();
        var propertyTypes = properties.Select(p =>
        {
            var pt = Substitute.For<IPropertyType>();
            pt.Alias.Returns(p.alias);
            pt.PropertyEditorAlias.Returns(p.editorAlias);
            return pt;
        }).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        contentType.CompositionPropertyTypes.Returns(propertyTypes);

        var service = Substitute.For<IContentTypeService>();
        service.Get(contentTypeAlias).Returns(contentType);
        return service;
    }

    [Fact]
    public void BrokenMediaLogoShape_WarnsOnce_ControlPropertySourcedLogoIsClean()
    {
        // Organization.Logo mapped as complexType/ImageObject with the persisted inner
        // binding ImageObject.Name <- 'logo', where 'logo' is a MediaPicker3: the media
        // resolves to a full ImageObject that the string-only ImageObject.Name silently
        // drops at render time — an empty {"@type":"ImageObject"} shell. Today only the
        // OUTER NestedSchemaTypeName is range-checked (ImageObject is in Logo's range, so
        // it validates allClear); the validator must inspect the inner complexTypeMappings
        // and warn.
        var contentTypeService = ContentTypeServiceWith(
            "siteSettings", ("logo", "Umbraco.MediaPicker3"));
        var sut = CreateValidator(contentTypeService);

        var broken = new SchemaMappingDto
        {
            ContentTypeAlias = "siteSettings",
            SchemaTypeName = "Organization",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Logo",
                    SourceType = "complexType",
                    NestedSchemaTypeName = "ImageObject",
                    ResolverConfig =
                        """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"logo"}]}"""
                }
            ]
        };

        var issues = sut.Validate(broken);

        issues.Should().ContainSingle(
            "binding a media-picker value onto string-only ImageObject.Name drops the logo at render time");
        issues[0].Severity.Should().Be(ValidationSeverity.Warning);
        issues[0].Path.Should().Be("Logo");
        issues[0].Message.Should().Contain("Logo");

        // Control: the healthy shape — a plain property-sourced logo mapping (the resolver
        // builds the ImageObject itself) — must NOT be flagged by the fix.
        var healthy = new SchemaMappingDto
        {
            ContentTypeAlias = "siteSettings",
            SchemaTypeName = "Organization",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Logo",
                    SourceType = "property",
                    ContentTypePropertyAlias = "logo"
                }
            ]
        };

        sut.Validate(healthy).Should().BeEmpty("property-sourced media logos are the correct shape");
    }
}
