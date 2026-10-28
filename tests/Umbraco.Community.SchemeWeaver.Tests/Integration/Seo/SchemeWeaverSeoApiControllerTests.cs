using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Seo;
using Umbraco.Community.SchemeWeaver.Seo.Authorization;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;
using Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Seo;

/// <summary>
/// Drives the SEO management API over HTTP on the shared TestHost. Authorisation is bypassed by
/// <see cref="TestPolicyEvaluator"/>, so the policy's registration is asserted separately.
/// </summary>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class SchemeWeaverSeoApiControllerTests : UmbracoIntegrationTestBase
{
    private const string SeoRoute = "/umbraco/management/api/v1/schemeweaver-seo";
    private const string CoreRoute = "/umbraco/management/api/v1/schemeweaver";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SchemeWeaverSeoApiControllerTests(SchemeWeaverWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        using var scope = CreateServiceScope();
        var scopeProvider = scope.ServiceProvider.GetRequiredService<IScopeProvider>();
        using (var dbScope = scopeProvider.CreateScope())
        {
            dbScope.Database.Execute($"DELETE FROM {SchemeWeaverSeoConstants.Tables.PageOverride}");
            dbScope.Complete();
        }

        scope.ServiceProvider.GetRequiredService<ISeoPageOverrideRepository>().ClearCache();
        scope.ServiceProvider.GetRequiredService<ISiteIdentityStore>().Delete();
    }

    [Fact]
    public async Task Templates_ReturnsTheCatalogue()
    {
        var dto = await Client.GetFromJsonAsync<SeoTemplateCatalogueDto>($"{SeoRoute}/templates", Json);
        dto!.Templates.Should().Contain(t => t.Id == "article");
        dto.Retired.Should().NotBeEmpty();
        dto.BusinessTypes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ServerContext_ReportsTheSectionAlias_MatchingTheFrontendManifest()
    {
        var dto = await Client.GetFromJsonAsync<SeoServerContextDto>($"{SeoRoute}/server-context", Json);
        dto!.SectionAlias.Should().Be(SchemeWeaverSeoConstants.SectionAlias);
        dto.SiteIdentityConfigured.Should().BeFalse();

        // The frontend constants file is the other half of the contract.
        var constants = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "src", "Umbraco.Community.SchemeWeaver.Seo", "App_Plugins", "SchemeWeaverSeo", "src", "constants.ts"));
        constants.Should().Contain($"'{SchemeWeaverSeoConstants.SectionAlias}'");
    }

    [Fact]
    public void SectionAccessPolicy_IsRegistered()
    {
        using var scope = CreateServiceScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = provider.GetPolicyAsync(SeoAuthorizationPolicies.SectionAccess).GetAwaiter().GetResult();
        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(r => r is SeoSectionRequirement && ((SeoSectionRequirement)r).SectionAlias == SchemeWeaverSeoConstants.SectionAlias);
    }

    [Fact]
    public async Task SiteIdentity_RoundTrips_AndFeedsTheGraph()
    {
        var alias = "seoIdentityProbe";
        await ProbeContentTypes.EnsureAsync(Factory.Services, alias, "SEO identity probe", ("title", "Title"));
        var key = await ProbeContent.EnsurePublishedAsync(Factory.Services, alias, "Identity probe page", ("title", "Hello"));
        var setup = await Client.PutAsJsonAsync($"{SeoRoute}/content-types/{alias}/mapping", new SeoMappingSaveRequest
        {
            TemplateId = "web-page",
            Slots = { ["name"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "__name" } },
        }, Json);
        setup.StatusCode.Should().Be(HttpStatusCode.OK);

        var bad = await Client.PutAsJsonAsync($"{SeoRoute}/site-identity", new SiteIdentityDto { Name = "" }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await Client.PutAsJsonAsync($"{SeoRoute}/site-identity", new SiteIdentityDto
        {
            Name = "Acme Ltd",
            SameAs = ["https://www.linkedin.com/company/acme"],
            BusinessType = "Dentist",
            Address = new SiteIdentityAddressDto { StreetAddress = "1 High St", AddressLocality = "Leeds", PostalCode = "LS1 1AA", AddressCountry = "GB" },
        }, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var back = await Client.GetFromJsonAsync<SiteIdentityDto>($"{SeoRoute}/site-identity", Json);
        back!.IsConfigured.Should().BeTrue();
        back.Name.Should().Be("Acme Ltd");

        var context = await Client.GetFromJsonAsync<SeoServerContextDto>($"{SeoRoute}/server-context", Json);
        context!.SiteIdentityConfigured.Should().BeTrue();

        var summary = await Client.GetFromJsonAsync<SeoPageSummaryDto>($"{SeoRoute}/pages/{key}/summary", Json);
        summary!.Preview.Should().NotBeNull();
        using var graph = JsonDocument.Parse(summary.Preview!.JsonLd);
        var organisation = graph.RootElement.GetProperty("@graph").EnumerateArray()
            .Single(n => n.GetProperty("@type").GetString() == "Dentist");
        organisation.GetProperty("name").GetString().Should().Be("Acme Ltd");
        organisation.GetProperty("@id").GetString().Should().EndWith("#organization");
        organisation.GetProperty("address").GetProperty("addressLocality").GetString().Should().Be("Leeds");
    }

    [Fact]
    public async Task ContentTypes_StatusMovesFromNotSetUp_ToSetUp_ThroughSetupAndSave()
    {
        var alias = "seoArticleProbe";
        await ProbeContentTypes.EnsureAsync(Factory.Services, alias, "SEO article probe", ("title", "Title"), ("summary", "Summary"), ("authorName", "Author name"));

        var before = (await Client.GetFromJsonAsync<List<SeoContentTypeStatusDto>>($"{SeoRoute}/content-types", Json))!
            .Single(c => c.Alias == alias);
        before.Status.Should().Be(SchemeWeaverSeoConstants.ContentTypeStatus.NotSetUp);
        before.IsElement.Should().BeFalse();

        var setupResponse = await Client.PostAsJsonAsync($"{SeoRoute}/content-types/{alias}/setup", new SeoSetupRequest { TemplateId = "article", SchemaTypeName = "BlogPosting" }, Json);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var projection = (await setupResponse.Content.ReadFromJsonAsync<SeoProjectionDto>(Json))!;
        projection.SchemaTypeName.Should().Be("BlogPosting");
        projection.Slots.Single(s => s.Slot.SchemaProperty == "headline").Value.PropertyAlias.Should().Be("title", "the heuristic auto-mapper's synonym match pre-fills the slot");
        projection.Slots.Single(s => s.Slot.SchemaProperty == "datePublished").Value.PropertyAlias.Should().Be("__createDate");

        var invalidVariant = await Client.PostAsJsonAsync($"{SeoRoute}/content-types/{alias}/setup", new SeoSetupRequest { TemplateId = "article", SchemaTypeName = "Recipe" }, Json);
        invalidVariant.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var request = new SeoMappingSaveRequest
        {
            TemplateId = "article",
            SchemaTypeName = "BlogPosting",
            Slots = projection.Slots.ToDictionary(s => s.Slot.SchemaProperty, s => s.Value, StringComparer.OrdinalIgnoreCase),
        };
        request.Slots["image"] = new SeoSlotValueDto { Kind = "none" };
        request.Slots["author"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "authorName" };
        request.Slots["description"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "summary" };

        var saveResponse = await Client.PutAsJsonAsync($"{SeoRoute}/content-types/{alias}/mapping", request, Json);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = (await saveResponse.Content.ReadFromJsonAsync<SeoContentTypeStatusDto>(Json))!;
        status.Mapping!.TemplateId.Should().Be("article");
        status.Mapping.RequiredFilled.Should().Be(3, "headline, datePublished and author are filled; image is not");
        status.Mapping.MissingRequired.Should().Equal("image");
        status.Status.Should().Be(SchemeWeaverSeoConstants.ContentTypeStatus.NeedsAttention);

        // The developer API sees the same rows: the SEO edition writes ordinary mappings.
        using var core = JsonDocument.Parse(await Client.GetStringAsync($"{CoreRoute}/mappings/{alias}"));
        core.RootElement.GetProperty("schemaTypeName").GetString().Should().Be("BlogPosting");
        var author = core.RootElement.GetProperty("propertyMappings").EnumerateArray()
            .Single(r => r.GetProperty("schemaPropertyName").GetString() == "author");
        author.GetProperty("sourceType").GetString().Should().Be("complexType");
        author.GetProperty("nestedSchemaTypeName").GetString().Should().Be("Person");

        var cleared = await Client.DeleteAsync($"{SeoRoute}/content-types/{alias}/mapping");
        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<SeoContentTypeStatusDto>($"{SeoRoute}/content-types/{alias}", Json))!
            .Status.Should().Be(SchemeWeaverSeoConstants.ContentTypeStatus.NotSetUp);
    }

    [Fact]
    public async Task PageOverride_RetypesAndExcludes_ThePublishedPage()
    {
        var alias = "seoPageProbe";
        await ProbeContentTypes.EnsureAsync(Factory.Services, alias, "SEO page probe", ("title", "Title"), ("authorName", "Author name"));
        var key = await ProbeContent.EnsurePublishedAsync(Factory.Services, alias, "Override probe page", ("title", "Hello"), ("authorName", "Jane"));

        // No mapping yet: an override cannot be saved.
        (await Client.PutAsJsonAsync($"{SeoRoute}/pages/{key}", new SeoPageOverrideRequest { IsExcluded = true }, Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Client.PutAsJsonAsync($"{SeoRoute}/content-types/{alias}/mapping", new SeoMappingSaveRequest
        {
            TemplateId = "article",
            Slots =
            {
                ["headline"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "title" },
                ["author"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "authorName" },
                ["datePublished"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "__createDate" },
            },
        }, Json);

        var summary = await Client.GetFromJsonAsync<SeoPageSummaryDto>($"{SeoRoute}/pages/{key}/summary", Json);
        summary!.HasMapping.Should().BeTrue();
        summary.TemplateId.Should().Be("article");
        summary.EffectiveSchemaTypeName.Should().Be("Article");
        summary.AllowedPageTypes.Select(v => v.SchemaType).Should().Contain("NewsArticle");
        summary.IsPublished.Should().BeTrue();
        MainEntityType(summary).Should().Be("Article");

        (await Client.PutAsJsonAsync($"{SeoRoute}/pages/{key}", new SeoPageOverrideRequest { SchemaTypeName = "Recipe" }, Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var retyped = await Client.PutAsJsonAsync($"{SeoRoute}/pages/{key}", new SeoPageOverrideRequest { SchemaTypeName = "NewsArticle" }, Json);
        retyped.StatusCode.Should().Be(HttpStatusCode.OK);
        summary = await Client.GetFromJsonAsync<SeoPageSummaryDto>($"{SeoRoute}/pages/{key}/summary", Json);
        summary!.EffectiveSchemaTypeName.Should().Be("NewsArticle");
        MainEntityType(summary).Should().Be("NewsArticle");
        MainEntityId(summary).Should().EndWith("#newsarticle");

        var excluded = await Client.PutAsJsonAsync($"{SeoRoute}/pages/{key}", new SeoPageOverrideRequest { IsExcluded = true }, Json);
        excluded.StatusCode.Should().Be(HttpStatusCode.OK);
        summary = await Client.GetFromJsonAsync<SeoPageSummaryDto>($"{SeoRoute}/pages/{key}/summary", Json);
        summary!.IsExcluded.Should().BeTrue();
        summary.Preview.Should().BeNull();

        // The live Delivery API path honours the exclusion too: no Article node, WebSite remains.
        var delivery = await Client.GetStringAsync($"/umbraco/delivery/api/v2/schemeweaver/json-ld?id={key}");
        delivery.Should().Contain("WebSite").And.NotContain("\"Article\"").And.NotContain("NewsArticle");

        (await Client.DeleteAsync($"{SeoRoute}/pages/{key}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"{SeoRoute}/pages/{key}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        summary = await Client.GetFromJsonAsync<SeoPageSummaryDto>($"{SeoRoute}/pages/{key}/summary", Json);
        MainEntityType(summary!).Should().Be("Article");
    }

    [Fact]
    public async Task HealthCheck_PreviewsTheSamplePage_AndCaches()
    {
        var alias = "seoHealthProbe";
        await ProbeContentTypes.EnsureAsync(Factory.Services, alias, "SEO health probe", ("title", "Title"));
        await ProbeContent.EnsurePublishedAsync(Factory.Services, alias, "Health probe page", ("title", "Hello"));
        await Client.PutAsJsonAsync($"{SeoRoute}/content-types/{alias}/mapping", new SeoMappingSaveRequest
        {
            TemplateId = "article",
            Slots = { ["headline"] = new SeoSlotValueDto { Kind = "property", PropertyAlias = "title" } },
        }, Json);

        var first = await (await Client.PostAsync($"{SeoRoute}/health-check?force=true", null)).Content.ReadFromJsonAsync<SeoHealthReportDto>(Json);
        first!.Cached.Should().BeFalse();
        var item = first.Items.Single(i => i.Alias == alias);
        item.Status.Should().Be("needs-attention");
        item.Critical.Should().BeGreaterThan(0, "image, datePublished and author are missing");
        item.SamplePageName.Should().Be("Health probe page");

        var second = await (await Client.PostAsync($"{SeoRoute}/health-check", null)).Content.ReadFromJsonAsync<SeoHealthReportDto>(Json);
        second!.Cached.Should().BeTrue();
        second.GeneratedAt.Should().Be(first.GeneratedAt);

        var samples = await Client.GetFromJsonAsync<List<SeoSamplePageDto>>($"{SeoRoute}/content-types/{alias}/sample-pages", Json);
        samples.Should().ContainSingle(s => s.Name == "Health probe page");
    }

    private static string MainEntityType(SeoPageSummaryDto summary)
    {
        using var doc = JsonDocument.Parse(summary.Preview!.JsonLd);
        return doc.RootElement.GetProperty("@graph").EnumerateArray()
            .Select(n => n.GetProperty("@type").GetString()!)
            .Single(t => t is "Article" or "NewsArticle" or "BlogPosting");
    }

    private static string MainEntityId(SeoPageSummaryDto summary)
    {
        using var doc = JsonDocument.Parse(summary.Preview!.JsonLd);
        return doc.RootElement.GetProperty("@graph").EnumerateArray()
            .Single(n => n.GetProperty("@type").GetString() is "Article" or "NewsArticle" or "BlogPosting")
            .GetProperty("@id").GetString()!;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Umbraco.Community.SchemeWeaver.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
