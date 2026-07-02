using FluentAssertions;
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
}
