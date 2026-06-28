using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Advisory;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Services.Validation;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemeWeaverServiceTests
{
    private readonly ISchemaTypeRegistry _registry = Substitute.For<ISchemaTypeRegistry>();
    private readonly ISchemaAutoMapper _autoMapper = Substitute.For<ISchemaAutoMapper>();
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();
    private readonly IGraphGenerator _graphGenerator = Substitute.For<IGraphGenerator>();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly IDataTypeService _dataTypeService = Substitute.For<IDataTypeService>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly IBlockSchemaSuggester _blockSchemaSuggester = Substitute.For<IBlockSchemaSuggester>();
    private readonly ISchemaRangeValidator _rangeValidator = Substitute.For<ISchemaRangeValidator>();
    private readonly IMappingAdvisor _advisor = Substitute.For<IMappingAdvisor>();
    private readonly IPropertyValueSchemaService _valueSchemaService = Substitute.For<IPropertyValueSchemaService>();
    private readonly IMappingReachabilityClassifier _reachabilityClassifier = Substitute.For<IMappingReachabilityClassifier>();
    private readonly IMappingDriftReporter _driftReporter = Substitute.For<IMappingDriftReporter>();
    private readonly IEventAggregator _eventAggregator = Substitute.For<IEventAggregator>();
    private readonly ILogger<SchemeWeaverService> _logger = Substitute.For<ILogger<SchemeWeaverService>>();

    // Existing preview tests assert against the legacy single-Thing string;
    // keep the graph model off so their assertions stay meaningful. A
    // dedicated @graph preview test below flips this on.
    private readonly SchemeWeaverOptions _options = new() { UseGraphModel = false };

    private readonly SchemeWeaverService _sut;

    public SchemeWeaverServiceTests()
    {
        // Default to an empty validation result — tests that care about issues
        // override this per-test. Without it, NSubstitute returns null and
        // ApplyValidation NPEs before the assertion runs.
        _validator.Validate(Arg.Any<string>()).Returns(ValidationResult.Empty);

        // Default to no structural warnings; tests that care override per-test.
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(Array.Empty<ValidationIssue>());

        // Default to no advisories; tests that care override per-test.
        _advisor.AdviseEntry(Arg.Any<MappingEntryInput>()).Returns(Array.Empty<MappingAdvice>());

        // Default drift: addon absent (usync-unavailable). Tests that care override per-test.
        _driftReporter.GetStatus(Arg.Any<string>()).Returns(MappingDriftStatus.USyncUnavailable);
        _driftReporter.GetReport().Returns(new MappingDriftReportDto { UsyncAvailable = false, Items = [] });

        _sut = new SchemeWeaverService(
            _registry, _autoMapper, _generator, _graphGenerator,
            _repository, _contentTypeService, _dataTypeService,
            _validator, _blockSchemaSuggester, _rangeValidator, _advisor, _valueSchemaService,
            _reachabilityClassifier, _driftReporter, _eventAggregator, Options.Create(_options), _logger);
    }

    [Fact]
    public void GetMapping_DelegatesToRepository()
    {
        var mapping = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.GetMapping("article");

        result.Should().NotBeNull();
        result!.ContentTypeAlias.Should().Be("article");
        result.SchemaTypeName.Should().Be("Article");
        _repository.Received(1).GetByContentTypeAlias("article");
    }

    [Fact]
    public void GetMapping_NoMapping_ReturnsNull()
    {
        _repository.GetByContentTypeAlias("unknown").Returns((SchemaMapping?)null);

        var result = _sut.GetMapping("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void AutoMap_DelegatesToAutoMapper()
    {
        var suggestions = new List<PropertyMappingSuggestion>
        {
            new() { SchemaPropertyName = "headline", Confidence = 100 }
        };
        _autoMapper.SuggestMappings("article", "Article").Returns(suggestions);

        var result = _sut.AutoMap("article", "Article").ToList();

        result.Should().HaveCount(1);
        _autoMapper.Received(1).SuggestMappings("article", "Article");
    }

    [Fact]
    public void GeneratePreview_DelegatesToGenerator()
    {
        var content = Substitute.For<IPublishedContent>();
        content.Id.Returns(1);
        _generator.GenerateJsonLdString(content).Returns("{\"@type\": \"Article\"}");

        var result = _sut.GeneratePreview(content);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.JsonLd.Should().Contain("Article");
        _generator.Received(1).GenerateJsonLdString(content);
    }

    [Fact]
    public void GeneratePreview_GraphModelEnabled_RoutesThroughGraphGenerator()
    {
        _options.UseGraphModel = true;
        var content = Substitute.For<IPublishedContent>();
        _graphGenerator.GenerateGraphJson(content, null).Returns("{\"@graph\":[{\"@type\":\"Organization\"}]}");

        var result = _sut.GeneratePreview(content);

        result.IsValid.Should().BeTrue();
        result.JsonLd.Should().Contain("@graph");
        _graphGenerator.Received(1).GenerateGraphJson(content, null);
        _generator.DidNotReceive().GenerateJsonLdString(Arg.Any<IPublishedContent>(), Arg.Any<string?>());
    }

    [Fact]
    public void SearchSchemaTypes_DelegatesToRegistry()
    {
        var types = new List<SchemaTypeInfo>
        {
            new() { Name = "Article" }
        };
        _registry.Search("Art").Returns(types);

        var result = _sut.SearchSchemaTypes("Art").ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Article");
        _registry.Received(1).Search("Art");
    }

    [Fact]
    public void SaveMapping_DelegatesToRepository()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>()
        };

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        _repository.Received(1).Save(Arg.Any<SchemaMapping>());
        _repository.Received(1).SavePropertyMappings(1, Arg.Any<IEnumerable<PropertyMapping>>());
    }

    [Fact]
    public void SaveMapping_WithMultipleProperties_PreservesAll()
    {
        // Round-trips a mapping containing many distinct property mappings
        // through SaveMapping → SavePropertyMappings to make sure none are
        // dropped along the way. This regression test exists because an early
        // E2E test corrupted seeded data by accidentally writing back a
        // single-property mapping; that case never reached the C# layer but
        // the safety net belongs here.
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>
            {
                new() { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "title" },
                new() { SchemaPropertyName = "description", SourceType = "property", ContentTypePropertyAlias = "summary" },
                new() { SchemaPropertyName = "image", SourceType = "property", ContentTypePropertyAlias = "heroImage" },
                new() { SchemaPropertyName = "author", SourceType = "static", StaticValue = "Editorial Team" },
                new()
                {
                    SchemaPropertyName = "publisher",
                    SourceType = "parent",
                    SourceContentTypeAlias = "siteRoot",
                    DynamicRootConfig = """{"originAlias":"Root","querySteps":[]}"""
                },
                new()
                {
                    SchemaPropertyName = "review",
                    SourceType = "blockContent",
                    ContentTypePropertyAlias = "reviews",
                    NestedSchemaTypeName = "Review",
                    ResolverConfig = """{"nestedMappings":[{"schemaProperty":"Author","contentProperty":"reviewAuthor"}]}"""
                },
            }
        };

        var savedEntity = new SchemaMapping
        {
            Id = 42,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(42).Returns(Enumerable.Empty<PropertyMapping>());

        List<PropertyMapping>? captured = null;
        _repository
            .When(r => r.SavePropertyMappings(42, Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => captured = c.Arg<IEnumerable<PropertyMapping>>().ToList());

        _sut.SaveMapping(dto);

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(6, "all six property mappings must reach the repository");
        captured.Select(m => m.SchemaPropertyName).Should().BeEquivalentTo(new[]
        {
            "headline", "description", "image", "author", "publisher", "review"
        });
        captured.Single(m => m.SchemaPropertyName == "publisher").DynamicRootConfig
            .Should().Be("""{"originAlias":"Root","querySteps":[]}""");
        captured.Single(m => m.SchemaPropertyName == "review").ResolverConfig
            .Should().Contain("reviewAuthor");
        captured.Single(m => m.SchemaPropertyName == "author").StaticValue
            .Should().Be("Editorial Team");
    }

    [Fact]
    public void SaveMapping_WithDynamicRootConfig_PersistsField()
    {
        const string dynamicRootJson = """{"originAlias":"Root","querySteps":[]}""";

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>
            {
                new()
                {
                    SchemaPropertyName = "publisher",
                    SourceType = "parent",
                    SourceContentTypeAlias = "organization",
                    DynamicRootConfig = dynamicRootJson
                }
            }
        };

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        List<PropertyMapping>? capturedMappings = null;
        _repository
            .When(r => r.SavePropertyMappings(1, Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => capturedMappings = c.Arg<IEnumerable<PropertyMapping>>().ToList());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        capturedMappings.Should().NotBeNull();
        capturedMappings!.Should().HaveCount(1);
        capturedMappings[0].SchemaPropertyName.Should().Be("publisher");
        capturedMappings[0].SourceType.Should().Be("parent");
        capturedMappings[0].SourceContentTypeAlias.Should().Be("organization");
        capturedMappings[0].DynamicRootConfig.Should().Be(dynamicRootJson);
    }

    [Fact]
    public void GetMapping_ReturnsDynamicRootConfig()
    {
        const string dynamicRootJson = """{"originAlias":"Root"}""";

        var mapping = new SchemaMapping
        {
            Id = 7,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 100,
                SchemaMappingId = 7,
                SchemaPropertyName = "publisher",
                SourceType = "parent",
                DynamicRootConfig = dynamicRootJson
            }
        };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(7).Returns(propertyMappings);

        var result = _sut.GetMapping("article");

        result.Should().NotBeNull();
        result!.PropertyMappings.Should().HaveCount(1);
        result.PropertyMappings[0].DynamicRootConfig.Should().Be(dynamicRootJson);
    }

    [Fact]
    public void SaveMapping_ResolvesContentTypeKey_WhenEmpty()
    {
        var expectedKey = Guid.NewGuid();
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            ContentTypeKey = Guid.Empty,
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>()
        };

        var contentType = Substitute.For<IContentType>();
        contentType.Key.Returns(expectedKey);
        _contentTypeService.Get("article").Returns(contentType);

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = expectedKey,
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        _contentTypeService.Received(1).Get("article");
        _repository.Received(1).Save(Arg.Is<SchemaMapping>(m => m.ContentTypeKey == expectedKey));
    }

    [Fact]
    public void SaveMapping_SetsReachabilityAndWarnings_AndPublishesSavedNotification()
    {
        var key = Guid.NewGuid();
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            ContentTypeKey = key,
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>()
        };

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = key,
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        _reachabilityClassifier.Classify("article").Returns("routed-page");
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(new[]
        {
            new ValidationIssue(ValidationSeverity.Warning, "Article", "HasPart", "out of range")
        });

        var result = _sut.SaveMapping(dto);

        result.Reachability.Should().Be("routed-page");
        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Severity.Should().Be("warning");
        result.Warnings[0].Path.Should().Be("HasPart");
        result.Warnings[0].Message.Should().Be("out of range");

        _eventAggregator.Received(1).Publish(Arg.Is<SchemaMappingSavedNotification>(
            n => n.ContentTypeAlias == "article" && n.ContentTypeKey == key));
    }

    [Fact]
    public void SaveMapping_InSyncAfterExport_SetsPersistedToDatabasePlusUsync()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = []
        };
        var savedEntity = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        // Export-on-save wrote the file: the post-publish drift check reports in-sync.
        _driftReporter.GetStatus("article").Returns(MappingDriftStatus.InSync);

        var result = _sut.SaveMapping(dto);

        result.DriftStatus.Should().Be(MappingDriftStatus.InSync);
        result.PersistedTo.Should().Be("database+usync");
    }

    [Fact]
    public void SaveMapping_NotOnDisk_SetsPersistedToDatabaseOnly()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = []
        };
        var savedEntity = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        // Export-on-save off (default): the mapping is in the DB only.
        _driftReporter.GetStatus("article").Returns(MappingDriftStatus.DbOnly);

        var result = _sut.SaveMapping(dto);

        result.DriftStatus.Should().Be(MappingDriftStatus.DbOnly);
        result.PersistedTo.Should().Be("database");
    }

    [Fact]
    public void GetMapping_SetsDriftStatus()
    {
        var mapping = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());
        _driftReporter.GetStatus("article").Returns(MappingDriftStatus.ContentDiffers);

        var result = _sut.GetMapping("article");

        result!.DriftStatus.Should().Be(MappingDriftStatus.ContentDiffers);
    }

    [Fact]
    public void GetAllMappings_UsesSingleDriftReport_NotPerItemStatus()
    {
        var mappings = new[]
        {
            new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true },
            new SchemaMapping { Id = 2, ContentTypeAlias = "newsItem", SchemaTypeName = "NewsArticle", IsEnabled = true }
        };
        _repository.GetAll().Returns(mappings);
        _repository.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>());
        _driftReporter.GetReport().Returns(new MappingDriftReportDto
        {
            UsyncAvailable = true,
            Items =
            [
                new MappingDriftEntryDto { ContentTypeAlias = "article", Status = MappingDriftStatus.InSync },
                new MappingDriftEntryDto { ContentTypeAlias = "newsItem", Status = MappingDriftStatus.DbOnly }
            ]
        });

        var result = _sut.GetAllMappings().ToList();

        result.Single(m => m.ContentTypeAlias == "article").DriftStatus.Should().Be(MappingDriftStatus.InSync);
        result.Single(m => m.ContentTypeAlias == "newsItem").DriftStatus.Should().Be(MappingDriftStatus.DbOnly);
        // The report is read once for the whole list, not per-mapping.
        _driftReporter.Received(1).GetReport();
        _driftReporter.DidNotReceive().GetStatus(Arg.Any<string>());
    }

    // --- v3 §3c: advisory wiring ---

    [Fact]
    public void GetMapping_AdvisorSuggestion_AppendedAfterRangeWarnings()
    {
        var mapping = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new[]
        {
            new PropertyMapping { SchemaPropertyName = "description", SourceType = "property", ContentTypePropertyAlias = "body" }
        });
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(new[]
        {
            new ValidationIssue(ValidationSeverity.Warning, "Article", "HasPart", "out of range")
        });
        _advisor.AdviseEntry(Arg.Any<MappingEntryInput>()).Returns(new[]
        {
            new MappingAdvice(MappingAdviceKind.StripHtml, "Article", "description", "emits raw HTML",
                new MappingAdviceFix(TransformType: "stripHtml"))
        });

        var result = _sut.GetMapping("article");

        // Range warning first (hard drop), advisory after, with severity "suggestion".
        result!.Warnings.Should().HaveCount(2);
        result.Warnings[0].Severity.Should().Be("warning");
        result.Warnings[0].Path.Should().Be("HasPart");
        result.Warnings[1].Severity.Should().Be("suggestion");
        result.Warnings[1].Path.Should().Be("description");
    }

    [Fact]
    public void GetMapping_AdvisoryOnRangeFlaggedRow_IsSuppressed()
    {
        var mapping = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new[]
        {
            new PropertyMapping { SchemaPropertyName = "hasPart", SourceType = "blockContent", ContentTypePropertyAlias = "blocks" }
        });
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(new[]
        {
            new ValidationIssue(ValidationSeverity.Warning, "Article", "hasPart", "out of range")
        });
        _advisor.AdviseEntry(Arg.Any<MappingEntryInput>()).Returns(new[]
        {
            new MappingAdvice(MappingAdviceKind.WrapInListItem, "Article", "hasPart", "no positions",
                new MappingAdviceFix(WrapInListItem: true))
        });

        var result = _sut.GetMapping("article");

        // The row is already a hard drop — don't double-flag it with a suggestion.
        result!.Warnings.Should().ContainSingle();
        result.Warnings[0].Severity.Should().Be("warning");
    }

    [Fact]
    public void SaveMapping_PersistenceAdvice_AppendedAsSuggestion()
    {
        var dto = new SchemaMappingDto { ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true, PropertyMappings = [] };
        var savedEntity = new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());
        _driftReporter.GetStatus("article").Returns(MappingDriftStatus.DbOnly);
        _advisor.AdvisePersistence(Arg.Any<string>(), Arg.Any<PersistenceFacts>())
            .Returns(new MappingAdvice(MappingAdviceKind.ExportToUSync, "Article", "(persistence)", "enable export"));

        var result = _sut.SaveMapping(dto);

        result.Warnings.Should().ContainSingle(w => w.Severity == "suggestion" && w.Message == "enable export");
    }

    [Fact]
    public void GetAllMappings_DoesNotInvokeAdvisor()
    {
        _repository.GetAll().Returns(new[]
        {
            new SchemaMapping { Id = 1, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true }
        });
        _repository.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>
        {
            [1] = [new PropertyMapping { SchemaPropertyName = "description", SourceType = "property", ContentTypePropertyAlias = "body" }]
        });
        _driftReporter.GetReport().Returns(new MappingDriftReportDto { UsyncAvailable = false, Items = [] });

        _ = _sut.GetAllMappings().ToList();

        // The many-mapping list view stays cheap — advisories are single-read/save only.
        _advisor.DidNotReceive().AdviseEntry(Arg.Any<MappingEntryInput>());
    }

    [Fact]
    public void DeleteMapping_PublishesDeletedNotification_WhenMappingExisted()
    {
        var key = Guid.NewGuid();
        var existing = new SchemaMapping { Id = 5, ContentTypeAlias = "article", ContentTypeKey = key };
        _repository.GetByContentTypeAlias("article").Returns(existing);

        _sut.DeleteMapping("article");

        _repository.Received(1).Delete(5);
        _eventAggregator.Received(1).Publish(Arg.Is<SchemaMappingDeletedNotification>(
            n => n.ContentTypeAlias == "article" && n.ContentTypeKey == key));
    }

    [Fact]
    public void DeleteMapping_DoesNotPublish_WhenNoMappingExisted()
    {
        _repository.GetByContentTypeAlias("ghost").Returns((SchemaMapping?)null);

        _sut.DeleteMapping("ghost");

        _repository.DidNotReceive().Delete(Arg.Any<int>());
        _eventAggregator.DidNotReceive().Publish(Arg.Any<SchemaMappingDeletedNotification>());
    }

    [Fact]
    public void GeneratePreview_SetsContextAndResolvedBaseUrl_AppendsStructuralWarnings_WithoutFlippingIsValid()
    {
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("article");
        var content = Substitute.For<IPublishedContent>();
        content.ContentType.Returns(contentType);
        _generator.GenerateJsonLdString(content).Returns("{\"@type\": \"Article\"}");
        _generator.GetResolvedBaseUrl().Returns("https://backoffice.example.com");

        // AppendStructuralWarnings re-reads the mapping for the alias.
        var mapping = new SchemaMapping { Id = 9, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(9).Returns(Enumerable.Empty<PropertyMapping>());
        _reachabilityClassifier.Classify("article").Returns(MappingReachabilityClassifier.ComposedFromBlock);
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(new[]
        {
            new ValidationIssue(ValidationSeverity.Warning, "Article", "HasPart", "out of range")
        });

        var result = _sut.GeneratePreview(content);

        result.IsValid.Should().BeTrue("structural warnings never flip the Rich Results validity flag");
        result.Context.Should().Be("backoffice-preview");
        result.ResolvedBaseUrl.Should().Be("https://backoffice.example.com");

        // One range warning + one hedged reachability warning.
        result.Issues.Should().Contain(i => i.Severity == "warning" && i.Path == "HasPart");
        result.Issues.Should().Contain(i =>
            i.Severity == "warning" && i.Message == MappingReachabilityClassifier.ComposedFromBlockWarning);
    }

    [Fact]
    public void GenerateMockPreview_SetsContextAndResolvedBaseUrl_AppendsWarnings()
    {
        var mapping = new SchemaMapping { Id = 3, ContentTypeAlias = "article", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(3).Returns(Enumerable.Empty<PropertyMapping>());
        _generator.GetResolvedBaseUrl().Returns("https://backoffice.example.com");
        _reachabilityClassifier.Classify("article").Returns("routed-page");
        _rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(new[]
        {
            new ValidationIssue(ValidationSeverity.Warning, "Article", "Author", "out of range")
        });

        var result = _sut.GenerateMockPreview("article");

        result.Context.Should().Be("backoffice-preview");
        result.ResolvedBaseUrl.Should().Be("https://backoffice.example.com");
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.Severity == "warning" && i.Path == "Author");
        // routed-page must NOT add the composed-from-block hedge.
        result.Issues.Should().NotContain(i => i.Message == MappingReachabilityClassifier.ComposedFromBlockWarning);
    }
}
