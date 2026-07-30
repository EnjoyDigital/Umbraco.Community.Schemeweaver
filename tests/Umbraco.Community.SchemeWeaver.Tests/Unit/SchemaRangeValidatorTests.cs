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
    public void MediaOntoImageObjectSubProperty_RenderAdopts_NoWarning_ControlPropertySourcedLogoIsClean()
    {
        // Organization.Logo mapped as complexType/ImageObject with the persisted inner binding
        // ImageObject.Name <- 'logo' (a MediaPicker3). At render time the media resolves to a
        // full ImageObject; because the nested type IS an ImageObject-family type, the render
        // (JsonLdGenerator.ResolveComplexTypeFromConfig) ADOPTS that ImageObject AS the nested
        // instance and emits a populated logo — see the agreeing render test
        // JsonLdGeneratorTests.MediaLogoAdoption_ValidatorAndRenderAgree. The validator must
        // therefore stay SILENT: warning "the media is dropped" here would tell the user to
        // break a mapping that renders correctly.
        var contentTypeService = ContentTypeServiceWith(
            "siteSettings", ("logo", "Umbraco.MediaPicker3"));
        var sut = CreateValidator(contentTypeService);

        var adopted = new SchemaMappingDto
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

        sut.Validate(adopted).Should().BeEmpty(
            "the render adopts the resolved media as the ImageObject nested instance, so nothing is dropped");

        // Control: the healthy shape — a plain property-sourced logo mapping (the resolver
        // builds the ImageObject itself) — must also be clean.
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

    [Fact]
    public void MediaOntoNonImageNestedType_RenderCannotAdopt_Warns()
    {
        // Author mapped as complexType/Person with the inner binding Person.Name <- 'authorPhoto'
        // (a MediaPicker3). Person is NOT an ImageObject-family type, so the render cannot adopt
        // the resolved media: a full ImageObject cannot be set onto the string-only Person.Name
        // and is silently dropped, leaving an empty {"@type":"Person"} shell. This is the mirror
        // of the ImageObject case above — here the warning MUST still fire.
        var contentTypeService = ContentTypeServiceWith(
            "article", ("authorPhoto", "Umbraco.MediaPicker3"));
        var sut = CreateValidator(contentTypeService);

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Author",
                    SourceType = "complexType",
                    NestedSchemaTypeName = "Person",
                    ResolverConfig =
                        """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"authorPhoto"}]}"""
                }
            ]
        };

        var issues = sut.Validate(dto);

        issues.Should().ContainSingle(
            "a media ImageObject dropped onto string-only Person.Name leaves an empty shell — the render cannot adopt into a non-image nested type");
        issues[0].Severity.Should().Be(ValidationSeverity.Warning);
        issues[0].Path.Should().Be("Author");
    }

    [Fact]
    public void TwoArgConstructorOverload_Constructs_AndValidateRuns()
    {
        // Binary-compat guard (3b): the 2-arg constructor must remain a distinct signature so
        // consumers precompiled against it don't hit MissingMethodException. Pin that it
        // constructs and Validate() runs (the editor-alias-dependent checks are simply skipped
        // because no IContentTypeService is supplied).
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        var sut = new SchemaRangeValidator(registry, new SchemaRangeChecker(registry));

        sut.Validate(Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Name",
            SourceType = "property",
            ContentTypePropertyAlias = "title"
        })).Should().BeEmpty();
    }

    [Fact]
    public void DrillDownConfig_OnNonPropertyRow_DoesNotSuppressRangeCheck()
    {
        // Drill config is only meaningful on property-sourced rows. A complexType
        // row that somehow carries drill-shaped JSON (e.g. a bad source switch)
        // renders via its nested type, so the range check must still fire.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "complexType",
            NestedSchemaTypeName = "Person", // out of range for HasPart
            ResolverConfig = """{"pickedPropertyAlias":"title"}"""
        });

        _sut.Validate(dto).Should().ContainSingle();
    }

    [Fact]
    public void DrillDownConfig_SuppressesNestedTypeRangeCheck()
    {
        // A picker row can carry BOTH nestedSchemaTypeName and a drill-down config
        // (authored via MCP/uSync). The render ignores the nested type when drilling,
        // so an out-of-range nested type must NOT warn — the warned-about output
        // never happens.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "HasPart",
            SourceType = "property",
            ContentTypePropertyAlias = "relatedNode",
            NestedSchemaTypeName = "Person", // out of range for HasPart (CreativeWork)
            ResolverConfig = """{"pickedPropertyAlias":"title"}"""
        });

        _sut.Validate(dto).Should().BeEmpty(
            "drill-down emits the picked property's value, not the nested type the range check would flag");
    }

    [Fact]
    public void AncestorSubRow_MediaOntoStringOnlySubProperty_WarnsUsingAncestorType()
    {
        // The media property lives on the ANCESTOR's content type (homePage.logo), not the
        // page's. The check must resolve the editor alias against the sub-row's
        // sourceContentTypeAlias — resolving against the page would silently skip.
        var contentTypeService = ContentTypeServiceWith(
            "homePage", ("logo", "Umbraco.MediaPicker3"));
        var sut = CreateValidator(contentTypeService);

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Author",
                    SourceType = "complexType",
                    NestedSchemaTypeName = "Person",
                    ResolverConfig =
                        """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"logo"}]}"""
                }
            ]
        };

        var issues = sut.Validate(dto);

        issues.Should().ContainSingle(
            "an ancestor-sourced media picker dropped onto string-only Person.Name is the same empty-shell trap as the page-local case");
        issues[0].Path.Should().Be("Author");
    }

    [Fact]
    public void ParentSubRow_MediaAlias_NoWarning_NoDeclaredTypeToResolveAgainst()
    {
        // parent sub-rows carry no content type alias — the check cannot know the
        // parent's type, so it must skip rather than guess (or false-positive off
        // the page's own same-named property).
        var contentTypeService = ContentTypeServiceWith(
            "article", ("logo", "Umbraco.MediaPicker3"));
        var sut = CreateValidator(contentTypeService);

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Author",
                    SourceType = "complexType",
                    NestedSchemaTypeName = "Person",
                    ResolverConfig =
                        """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"parent","contentTypePropertyAlias":"logo"}]}"""
                }
            ]
        };

        sut.Validate(dto).Should().BeEmpty(
            "a parent sub-row has no declared source type, so the media check must not fire off the page's same-named property");
    }

    [Fact]
    public void AncestorSubRow_UnknownSubProperty_StillWarns()
    {
        // The existing unknown-sub-property warning must keep firing for related-node
        // sub-rows too (the dispatch change must not skip the shared checks).
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Author",
            SourceType = "complexType",
            NestedSchemaTypeName = "Person",
            ResolverConfig =
                """{"complexTypeMappings":[{"schemaProperty":"NoSuchProperty","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"organisationName"}]}"""
        });

        var issues = _sut.Validate(dto);

        issues.Should().ContainSingle();
        issues[0].Message.Should().Contain("NoSuchProperty");
    }

    [Fact]
    public void UnknownTopLevelProperty_Warns()
    {
        // A property that doesn't exist on the type is log-and-skipped by the setter,
        // so it emits nothing at all. Previously this was passed over in silence —
        // the state a schema-type change (or an API/uSync write) can leave behind.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "CookTime",
            SourceType = "property",
            ContentTypePropertyAlias = "duration"
        });

        var issues = _sut.Validate(dto);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(ValidationSeverity.Warning);
        issues[0].Path.Should().Be("CookTime");
        issues[0].Message.Should().Contain("CookTime");
        issues[0].Message.Should().Contain("Article");
        issues[0].Message.Should().Contain("dropped");
    }

    [Fact]
    public void KnownTopLevelProperty_DoesNotWarn()
    {
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "Headline",
            SourceType = "property",
            ContentTypePropertyAlias = "title"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void UnknownTopLevelProperty_MatchesCaseInsensitively()
    {
        // Stored mappings vary in casing; only a genuinely absent property warns.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "headline",
            SourceType = "property",
            ContentTypePropertyAlias = "title"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void UnknownTopLevelProperty_OnAReferenceRow_DoesNotWarn()
    {
        // reference rows resolve through another graph piece and are deliberately
        // skipped before any property lookup — that must not change.
        var dto = Article(new PropertyMappingDto
        {
            SchemaPropertyName = "NoSuchProperty",
            SourceType = "reference",
            TargetPieceKey = "organization"
        });

        _sut.Validate(dto).Should().BeEmpty();
    }
}
