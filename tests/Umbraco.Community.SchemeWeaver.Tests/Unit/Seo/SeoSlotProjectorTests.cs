using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Services;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Seo.SchemeWeaverSeoConstants;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

public class SeoSlotProjectorTests
{
    private readonly SeoSlotProjector _sut = new();
    private readonly SeoTemplate _article = SeoTestSupport.Catalogue.Get("article")!;

    private static readonly Dictionary<string, string> Editors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "Umbraco.TextBox",
        ["body"] = "Umbraco.RichText",
        ["heroImage"] = "Umbraco.MediaPicker3",
        ["authorName"] = "Umbraco.TextBox",
        ["authorPage"] = "Umbraco.ContentPicker",
        ["publishedOn"] = "Umbraco.DateTime",
        ["__name"] = "SchemeWeaver.BuiltIn",
        ["__url"] = "SchemeWeaver.BuiltIn",
        ["__createDate"] = "SchemeWeaver.BuiltIn",
        ["__updateDate"] = "SchemeWeaver.BuiltIn",
    };

    private static PropertyMappingSuggestion Suggest(string prop, string alias, int confidence, string editor = "Umbraco.TextBox") => new()
    {
        SchemaPropertyName = prop,
        SuggestedContentTypePropertyAlias = alias,
        SuggestedSourceType = "property",
        Confidence = confidence,
        IsAutoMapped = confidence >= 80,
        EditorAlias = editor,
    };

    [Fact]
    public void Project_NoMapping_FillsFromSuggestionsThenDefaults()
    {
        var suggestions = new List<PropertyMappingSuggestion>
        {
            Suggest("headline", "title", 100),
            Suggest("image", "heroImage", 95, "Umbraco.MediaPicker3"),
            Suggest("description", "body", 55, "Umbraco.RichText"), // below the show threshold
            new() { SchemaPropertyName = "publisher", SuggestedSourceType = "reference", SuggestedTargetPieceKey = "organization", Confidence = 90 },
        };

        var projection = _sut.Project(_article, "BlogPosting", null, suggestions, Editors, siteIdentityConfigured: true);

        projection.HasExistingMapping.Should().BeFalse();
        projection.SchemaTypeName.Should().Be("BlogPosting");
        Value(projection, "headline").Should().BeEquivalentTo(new { Kind = SlotValueKinds.Property, PropertyAlias = "title", Confidence = 100 });
        Value(projection, "image").PropertyAlias.Should().Be("heroImage");
        Value(projection, "description").Kind.Should().Be(SlotValueKinds.None, "weak suggestions are not pre-filled");
        Value(projection, "publisher").Should().BeEquivalentTo(new { Kind = SlotValueKinds.SiteIdentity, TargetPieceKey = "organization" });
        Value(projection, "datePublished").Should().BeEquivalentTo(new { Kind = SlotValueKinds.Property, PropertyAlias = "__createDate" }, "slot defaults fill the rest");
        Value(projection, "dateModified").PropertyAlias.Should().Be("__updateDate");
        Value(projection, "author").Kind.Should().Be(SlotValueKinds.None);
    }

    [Fact]
    public void Project_SiteIdentityNotConfigured_DoesNotDefaultToReference()
    {
        var projection = _sut.Project(_article, "Article", null, [], Editors, siteIdentityConfigured: false);
        Value(projection, "publisher").Kind.Should().Be(SlotValueKinds.None);
    }

    [Fact]
    public void ToMappingDto_WritesOrdinaryRows_AndNestsATextFieldForAnEntitySlot()
    {
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "article",
            SchemaTypeName = "BlogPosting",
            Slots =
            {
                ["headline"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "title", IsAutoMapped = true },
                ["description"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "body" },
                ["author"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "authorName" },
                ["publisher"] = new SeoSlotValueDto { Kind = SlotValueKinds.SiteIdentity },
                ["datePublished"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "__createDate" },
                ["dateModified"] = new SeoSlotValueDto { Kind = SlotValueKinds.None },
            },
        };

        var dto = _sut.ToMappingDto(_article, "blogPost", Guid.NewGuid(), "BlogPosting", request, null, Editors);

        dto.SchemaTypeName.Should().Be("BlogPosting");
        dto.IsEnabled.Should().BeTrue();
        Row(dto, "headline").Should().BeEquivalentTo(new { SourceType = "property", ContentTypePropertyAlias = "title", IsAutoMapped = true, TransformType = (string?)null });
        Row(dto, "description").TransformType.Should().Be("stripHtml", "a rich text editor feeding a text property is stripped");
        var author = Row(dto, "author");
        author.SourceType.Should().Be("complexType");
        author.NestedSchemaTypeName.Should().Be("Person");
        using var config = JsonDocument.Parse(author.ResolverConfig!);
        var entry = config.RootElement.GetProperty("complexTypeMappings")[0];
        entry.GetProperty("schemaProperty").GetString().Should().Be("name");
        entry.GetProperty("contentTypePropertyAlias").GetString().Should().Be("authorName");
        Row(dto, "publisher").Should().BeEquivalentTo(new { SourceType = "reference", TargetPieceKey = "organization" });
        dto.PropertyMappings.Should().NotContain(r => r.SchemaPropertyName == "dateModified");
    }

    [Fact]
    public void ToMappingDto_PickerOnEntitySlot_RendersThePickedNode()
    {
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "article",
            Slots = { ["author"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "authorPage" } },
        };

        var dto = _sut.ToMappingDto(_article, "blogPost", Guid.NewGuid(), "Article", request, null, Editors);

        Row(dto, "author").Should().BeEquivalentTo(new { SourceType = "property", ContentTypePropertyAlias = "authorPage", NestedSchemaTypeName = "Person" });
    }

    [Fact]
    public void ToMappingDto_NestedValues_BecomeAComplexTypeRow_TheGeneratorCanRead()
    {
        var product = SeoTestSupport.Catalogue.Get("product")!;
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "product",
            Slots =
            {
                ["name"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "__name" },
                ["offers"] = new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Nested,
                    Nested =
                    {
                        ["price"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "price" },
                        ["priceCurrency"] = new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = "GBP" },
                    },
                },
            },
        };

        var dto = _sut.ToMappingDto(product, "product", Guid.NewGuid(), "Product", request, null, Editors);

        var offers = Row(dto, "offers");
        offers.SourceType.Should().Be("complexType");
        offers.NestedSchemaTypeName.Should().Be("Offer");
        var parsed = JsonSerializer.Deserialize<Services.Resolvers.ComplexTypeConfigModel>(
            offers.ResolverConfig!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        parsed.ComplexTypeMappings.Should().HaveCount(2);
        parsed.ComplexTypeMappings![0].Should().BeEquivalentTo(new { SchemaProperty = "price", SourceType = "property", ContentTypePropertyAlias = "price" });
        parsed.ComplexTypeMappings[1].Should().BeEquivalentTo(new { SchemaProperty = "priceCurrency", SourceType = "static", StaticValue = "GBP" });
    }

    [Fact]
    public void RoundTrip_IsLossless_AndPreservesDeveloperRows()
    {
        var existing = new SchemaMappingDto
        {
            ContentTypeAlias = "blogPost",
            ContentTypeKey = Guid.NewGuid(),
            SchemaTypeName = "NewsArticle",
            IsEnabled = true,
            IsInherited = true,
            IdOverride = "{url}#news",
            PropertyMappings =
            [
                new() { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "title", IsAutoMapped = true },
                new() { SchemaPropertyName = "author", SourceType = "complexType", NestedSchemaTypeName = "Person", ResolverConfig = """{"complexTypeMappings":[{"schemaProperty":"name","sourceType":"property","contentTypePropertyAlias":"authorName"},{"schemaProperty":"url","sourceType":"static","staticValue":"https://example.com/jane"}]}""" },
                new() { SchemaPropertyName = "publisher", SourceType = "reference", TargetPieceKey = "organization" },
                // A developer's cross-node row on a slot: shown as advanced, preserved verbatim.
                new() { SchemaPropertyName = "image", SourceType = "ancestor", ContentTypePropertyAlias = "sectionImage", SourceContentTypeAlias = "section" },
                // A developer's block mapping the kind knows nothing about.
                new() { SchemaPropertyName = "hasPart", SourceType = "blockContent", ContentTypePropertyAlias = "content", NestedSchemaTypeName = "WebPageElement", ResolverConfig = """{"routes":[]}""" },
                new() { SchemaPropertyName = "keywords", SourceType = "property", ContentTypePropertyAlias = "tags" },
            ],
        };

        var projection = _sut.Project(_article, existing.SchemaTypeName, existing, null, Editors, siteIdentityConfigured: true);

        Value(projection, "headline").PropertyAlias.Should().Be("title");
        Value(projection, "author").Kind.Should().Be(SlotValueKinds.Nested);
        Value(projection, "author").Nested["name"].PropertyAlias.Should().Be("authorName");
        Value(projection, "author").Nested["url"].FixedValue.Should().Be("https://example.com/jane");
        Value(projection, "publisher").Kind.Should().Be(SlotValueKinds.SiteIdentity);
        Value(projection, "image").Kind.Should().Be(SlotValueKinds.Advanced);
        projection.AdvancedRows.Select(r => r.SchemaPropertyName).Should().BeEquivalentTo(["hasPart", "keywords"]);

        // Save back exactly what was projected (the UI's "no edits" case).
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "article",
            SchemaTypeName = existing.SchemaTypeName,
            Slots = projection.Slots.ToDictionary(s => s.Slot.SchemaProperty, s => s.Value, StringComparer.OrdinalIgnoreCase),
        };
        var saved = _sut.ToMappingDto(_article, "blogPost", Guid.NewGuid(), existing.SchemaTypeName, request, existing, Editors);

        saved.IsInherited.Should().BeTrue();
        saved.IdOverride.Should().Be("{url}#news");
        saved.ContentTypeKey.Should().Be(existing.ContentTypeKey);
        saved.PropertyMappings.Should().BeEquivalentTo(existing.PropertyMappings, o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void Summarise_CountsRequiredAndRecommended_WithAnyOfGroups()
    {
        var product = SeoTestSupport.Catalogue.Get("product")!;
        var mapping = new SchemaMappingDto
        {
            SchemaTypeName = "Product",
            IsEnabled = true,
            PropertyMappings =
            [
                new() { SchemaPropertyName = "name", SourceType = "property", ContentTypePropertyAlias = "__name" },
                new() { SchemaPropertyName = "review", SourceType = "blockContent", ContentTypePropertyAlias = "reviews" },
                new() { SchemaPropertyName = "image", SourceType = "property", ContentTypePropertyAlias = "photo" },
                new() { SchemaPropertyName = "color", SourceType = "property", ContentTypePropertyAlias = "colour" },
            ],
        };

        var summary = _sut.Summarise(product, mapping);

        summary.RequiredTotal.Should().Be(2, "name plus the eligibility group");
        summary.RequiredFilled.Should().Be(2, "review satisfies the eligibility group");
        summary.MissingRequired.Should().BeEmpty();
        summary.RecommendedFilled.Should().Be(1);
        summary.AdvancedRowCount.Should().Be(1);

        mapping.PropertyMappings.RemoveAll(r => r.SchemaPropertyName == "review");
        _sut.Summarise(product, mapping).MissingRequired.Should().ContainSingle(m => m.Contains("offers"));
    }

    [Fact]
    public void ToMappingDto_NestedAddressInsideJobLocation_RoundTrips_AsNestedComplexConfig()
    {
        var job = SeoTestSupport.Catalogue.Get("job-posting")!;
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "job-posting",
            Slots =
            {
                ["title"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "__name" },
                ["jobLocation"] = new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Nested,
                    Nested =
                    {
                        ["name"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "title" },
                        ["address"] = new SeoSlotValueDto
                        {
                            Kind = SlotValueKinds.Nested,
                            Nested =
                            {
                                ["addressLocality"] = new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = "title" },
                                ["addressCountry"] = new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = "GB" },
                            },
                        },
                    },
                },
            },
        };

        var dto = _sut.ToMappingDto(job, "job", Guid.NewGuid(), "JobPosting", request, null, Editors);

        var location = Row(dto, "jobLocation");
        location.SourceType.Should().Be("complexType");
        location.NestedSchemaTypeName.Should().Be("Place");
        var outer = JsonSerializer.Deserialize<Services.Resolvers.ComplexTypeConfigModel>(location.ResolverConfig!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var address = outer.ComplexTypeMappings!.Single(e => e.SchemaProperty == "address");
        address.SourceType.Should().Be("complexType");
        var inner = JsonSerializer.Deserialize<Services.Resolvers.ComplexTypeConfigModel>(address.ResolverConfig!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        inner.SelectedSubType.Should().Be("PostalAddress");
        inner.ComplexTypeMappings.Should().Contain(e => e.SchemaProperty == "addressCountry" && e.StaticValue == "GB");

        // And it projects back without turning into an advanced row.
        var projection = _sut.Project(job, "JobPosting", dto, null, Editors, siteIdentityConfigured: false);
        var value = Value(projection, "jobLocation");
        value.Kind.Should().Be(SlotValueKinds.Nested);
        value.Nested["address"].Kind.Should().Be(SlotValueKinds.Nested);
        value.Nested["address"].Nested["addressCountry"].FixedValue.Should().Be("GB");
    }

    [Fact]
    public void ToMappingDto_MatchesSlotKeysCaseInsensitively_EvenWithADefaultComparerDictionary()
    {
        // System.Text.Json rebuilds Slots with the default comparer; keys from a client may be PascalCase.
        var request = new SeoMappingSaveRequest
        {
            TemplateId = "article",
            Slots = new Dictionary<string, SeoSlotValueDto>
            {
                ["Headline"] = new() { Kind = SlotValueKinds.Property, PropertyAlias = "title" },
                ["Author"] = new() { Kind = SlotValueKinds.Nested, Nested = new Dictionary<string, SeoSlotValueDto> { ["Name"] = new() { Kind = SlotValueKinds.Property, PropertyAlias = "authorName" } } },
            },
        };

        var dto = _sut.ToMappingDto(_article, "blogPost", Guid.NewGuid(), "Article", request, null, Editors);

        Row(dto, "headline").ContentTypePropertyAlias.Should().Be("title");
        Row(dto, "author").ResolverConfig.Should().Contain("authorName");
    }

    [Fact]
    public void ToMappingDto_PreservesADisabledMapping()
    {
        var existing = new SchemaMappingDto { ContentTypeAlias = "blogPost", SchemaTypeName = "Article", IsEnabled = false };
        var dto = _sut.ToMappingDto(_article, "blogPost", Guid.NewGuid(), "Article", new SeoMappingSaveRequest { TemplateId = "article" }, existing, Editors);
        dto.IsEnabled.Should().BeFalse("a developer-disabled mapping is not silently re-enabled");
    }

    private static SeoSlotValueDto Value(SeoProjectionDto projection, string property) =>
        projection.Slots.Single(s => s.Slot.SchemaProperty == property).Value;

    private static PropertyMappingDto Row(SchemaMappingDto dto, string property) =>
        dto.PropertyMappings.Single(r => r.SchemaPropertyName == property);
}
