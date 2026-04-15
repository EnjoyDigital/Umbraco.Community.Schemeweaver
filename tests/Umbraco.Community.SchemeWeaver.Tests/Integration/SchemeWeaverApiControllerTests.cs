using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="Controllers.SchemeWeaverApiController"/>. Drives
/// the real management API over HTTP via <see cref="SchemeWeaverWebApplicationFactory"/>,
/// with authorization bypassed by <see cref="TestPolicyEvaluator"/> so tests can call
/// protected endpoints directly.
/// </summary>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class SchemeWeaverApiControllerTests : UmbracoIntegrationTestBase
{
    private const string BaseRoute = "/umbraco/management/api/v1/schemeweaver";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SchemeWeaverApiControllerTests(SchemeWeaverWebApplicationFactory factory)
        : base(factory)
    {
    }

    // --------------------------------------------------------------------
    // Schema types
    // --------------------------------------------------------------------

    [Fact]
    public async Task GetSchemaTypes_ReturnsOkWithNonEmptyList()
    {
        var response = await Client.GetAsync($"{BaseRoute}/schema-types");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0,
            "Schema.NET exposes hundreds of schema types that should be discoverable");
    }

    [Fact]
    public async Task GetSchemaTypeProperties_DefaultResponse_MatchesLegacyShape()
    {
        // Back-compat guardrail: the default (no-query-param) response is consumed
        // by the existing frontend and MUST NOT gain new fields. If we want to ship
        // ranking, it has to live behind ?ranked=true.
        var response = await Client.GetAsync($"{BaseRoute}/schema-types/Article/properties");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        var firstItem = doc.RootElement.EnumerateArray().First();
        firstItem.TryGetProperty("confidence", out _).Should().BeFalse(
            "legacy response must not expose confidence");
        firstItem.TryGetProperty("isPopular", out _).Should().BeFalse(
            "legacy response must not expose isPopular");
    }

    [Fact]
    public async Task GetSchemaTypeProperties_RankedTrue_ReturnsRankedShape()
    {
        var response = await Client.GetAsync($"{BaseRoute}/schema-types/Article/properties?ranked=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        var items = doc.RootElement.EnumerateArray().ToList();
        var firstItem = items.First();
        firstItem.TryGetProperty("confidence", out _).Should().BeTrue(
            "ranked response must expose confidence");
        firstItem.TryGetProperty("isPopular", out _).Should().BeTrue(
            "ranked response must expose isPopular");

        var firstConfidence = items.First().GetProperty("confidence").GetInt32();
        var lastConfidence = items.Last().GetProperty("confidence").GetInt32();
        firstConfidence.Should().BeGreaterThanOrEqualTo(lastConfidence,
            "results must be sorted by confidence descending");
    }

    [Fact]
    public async Task GetSchemaTypes_WithSearchQuery_FiltersResults()
    {
        var response = await Client.GetAsync($"{BaseRoute}/schema-types?search=BlogPosting");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .Should().Contain("BlogPosting");
    }

    // --------------------------------------------------------------------
    // Mapping CRUD
    // --------------------------------------------------------------------

    [Fact]
    public async Task GetMapping_ExistingAlias_ReturnsOkWithDto()
    {
        SeedMapping("blogPost", "BlogPosting");

        var response = await Client.GetAsync($"{BaseRoute}/mappings/blogPost");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SchemaMappingDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.ContentTypeAlias.Should().Be("blogPost");
        dto.SchemaTypeName.Should().Be("BlogPosting");
    }

    [Fact]
    public async Task GetMapping_NonExistingAlias_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"{BaseRoute}/mappings/doesNotExist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMappings_ReturnsAllSeededMappings()
    {
        SeedMapping("blogPost", "BlogPosting");
        SeedMapping("product", "Product");

        var response = await Client.GetAsync($"{BaseRoute}/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<SchemaMappingDto>>(JsonOptions);
        dtos.Should().NotBeNull();
        dtos!.Should().HaveCount(2);
        dtos.Select(x => x.ContentTypeAlias).Should().BeEquivalentTo(new[] { "blogPost", "product" });
    }

    [Fact]
    public async Task SaveMapping_ValidDto_ReturnsOkAndPersists()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            ContentTypeKey = Guid.NewGuid(),
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "headline",
                    SourceType = "property",
                    ContentTypePropertyAlias = "title",
                },
                new PropertyMappingDto
                {
                    SchemaPropertyName = "author",
                    SourceType = "static",
                    StaticValue = "Jane Smith",
                },
            ],
        };

        var response = await Client.PostAsJsonAsync($"{BaseRoute}/mappings", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Round-trip through the repository to confirm persistence.
        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var persisted = repository.GetByContentTypeAlias("article");
            persisted.Should().NotBeNull();
            persisted!.SchemaTypeName.Should().Be("Article");

            var props = repository.GetPropertyMappings(persisted.Id).ToList();
            props.Should().HaveCount(2);
            props.Select(x => x.SchemaPropertyName)
                .Should().BeEquivalentTo(new[] { "headline", "author" });
        }
    }

    [Fact]
    public async Task SaveMapping_MissingContentTypeAlias_ReturnsBadRequest()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "",
            SchemaTypeName = "Article",
        };

        var response = await Client.PostAsJsonAsync($"{BaseRoute}/mappings", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SaveMapping_MissingSchemaTypeName_ReturnsBadRequest()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "",
        };

        var response = await Client.PostAsJsonAsync($"{BaseRoute}/mappings", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteMapping_RemovesRowAndReturnsNoContent()
    {
        SeedMapping("event", "Event");

        var response = await Client.DeleteAsync($"{BaseRoute}/mappings/event");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            repository.GetByContentTypeAlias("event").Should().BeNull();
        }
    }

    // --------------------------------------------------------------------
    // Auto-map and preview
    // --------------------------------------------------------------------

    [Fact]
    public async Task AutoMap_MissingSchemaTypeName_ReturnsBadRequest()
    {
        // Empty schemaTypeName hits the validation branch before any content type
        // lookup, so we don't need to seed a real Umbraco content type.
        var response = await Client.PostAsync(
            $"{BaseRoute}/mappings/blogPost/auto-map?schemaTypeName=",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_WithoutContentKey_ReturnsOkWithMockPreview()
    {
        SeedMapping("blogPost", "BlogPosting");

        // Without contentKey the controller calls GenerateMockPreview, which only
        // needs the saved mapping — no real content item is required.
        var response = await Client.PostAsync(
            $"{BaseRoute}/mappings/blogPost/preview",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact(Skip = "Requires variant seed from feature/variants-testhost")]
    public async Task Preview_WithCultureParam_ReturnsOkWithCulturedPreview()
    {
        // This test needs variant content seeded in the TestHost (Worktree B).
        // Once merged, it should call POST /preview?contentKey=...&culture=de-DE
        // and verify the JSON-LD contains German values and inLanguage: "de-DE".
        var response = await Client.PostAsync(
            $"{BaseRoute}/mappings/blogPost/preview?culture=de-DE",
            content: null);

        // For now just verify the endpoint accepts the culture parameter
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound);
    }

    // --------------------------------------------------------------------
    // End-to-end round trip
    // --------------------------------------------------------------------

    [Fact]
    public async Task RoundTrip_SaveGetDelete_BehavesConsistently()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "recipe",
            ContentTypeKey = Guid.NewGuid(),
            SchemaTypeName = "Recipe",
            IsEnabled = true,
        };

        // Save
        var saveResponse = await Client.PostAsJsonAsync($"{BaseRoute}/mappings", dto);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get
        var getResponse = await Client.GetAsync($"{BaseRoute}/mappings/recipe");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var persisted = await getResponse.Content.ReadFromJsonAsync<SchemaMappingDto>(JsonOptions);
        persisted.Should().NotBeNull();
        persisted!.SchemaTypeName.Should().Be("Recipe");

        // Delete
        var deleteResponse = await Client.DeleteAsync($"{BaseRoute}/mappings/recipe");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm gone
        var missingResponse = await Client.GetAsync($"{BaseRoute}/mappings/recipe");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

}
