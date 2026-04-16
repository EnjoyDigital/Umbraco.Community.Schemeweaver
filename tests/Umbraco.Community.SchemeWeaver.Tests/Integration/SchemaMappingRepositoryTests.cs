using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="Persistence.SchemaMappingRepository"/>.
/// Exercises NPoco CRUD against a real SQLite database booted via
/// <see cref="SchemeWeaverWebApplicationFactory"/>.
/// </summary>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class SchemaMappingRepositoryTests : UmbracoIntegrationTestBase
{
    public SchemaMappingRepositoryTests(SchemeWeaverWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void GetAll_EmptyDatabase_ReturnsEmpty()
    {
        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var results = repository.GetAll();

            results.Should().BeEmpty();
        }
    }

    [Fact]
    public void GetAll_WithMappings_ReturnsAllInsertedRows()
    {
        SeedMapping("blogPost", "BlogPosting");
        SeedMapping("product", "Product");
        SeedMapping("article", "Article");

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var results = repository.GetAll().ToList();

            results.Should().HaveCount(3);
            results.Select(x => x.ContentTypeAlias)
                .Should().BeEquivalentTo(new[] { "blogPost", "product", "article" });
        }
    }

    [Fact]
    public void GetByContentTypeAlias_ExistingAlias_ReturnsMapping()
    {
        SeedMapping("blogPost", "BlogPosting");

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var result = repository.GetByContentTypeAlias("blogPost");

            result.Should().NotBeNull();
            result!.ContentTypeAlias.Should().Be("blogPost");
            result.SchemaTypeName.Should().Be("BlogPosting");
            result.IsEnabled.Should().BeTrue();
            result.Id.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void GetByContentTypeAlias_NonExistingAlias_ReturnsNull()
    {
        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var result = repository.GetByContentTypeAlias("doesNotExist");

            result.Should().BeNull();
        }
    }

    [Fact]
    public void Save_NewMapping_AssignsIdAndPersists()
    {
        var mapping = new SchemaMapping
        {
            ContentTypeAlias = "event",
            ContentTypeKey = Guid.NewGuid(),
            SchemaTypeName = "Event",
            IsEnabled = true,
        };

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var saved = repository.Save(mapping);

            saved.Id.Should().BeGreaterThan(0);
            saved.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            saved.UpdatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

            var roundTrip = repository.GetByContentTypeAlias("event");
            roundTrip.Should().NotBeNull();
            roundTrip!.SchemaTypeName.Should().Be("Event");
        }
    }

    [Fact]
    public void Save_ExistingMapping_UpdatesInPlaceWithoutDuplicate()
    {
        var seeded = SeedMapping("recipe", "Recipe");
        var originalCreated = seeded.CreatedDate;

        // Small delay so the UpdatedDate is definitely different from CreatedDate.
        Thread.Sleep(20);

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            seeded.SchemaTypeName = "HowTo";
            var updated = repository.Save(seeded);

            updated.Id.Should().Be(seeded.Id);
            updated.SchemaTypeName.Should().Be("HowTo");
            updated.UpdatedDate.Should().BeOnOrAfter(originalCreated);

            var all = repository.GetAll().ToList();
            all.Should().HaveCount(1);
            all[0].SchemaTypeName.Should().Be("HowTo");
        }
    }

    [Fact]
    public void Delete_RemovesMappingAndCascadesPropertyMappings()
    {
        var seeded = SeedMapping("faq", "FAQPage", propertyMappings:
        [
            new PropertyMapping
            {
                SchemaPropertyName = "mainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "faqItems",
                IsAutoMapped = true,
            },
        ]);

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            repository.GetPropertyMappings(seeded.Id).Should().HaveCount(1);

            repository.Delete(seeded.Id);

            repository.GetByContentTypeAlias("faq").Should().BeNull();
            repository.GetPropertyMappings(seeded.Id).Should().BeEmpty();
        }
    }

    [Fact]
    public void GetPropertyMappings_ReturnsOnlyRowsForGivenSchemaMappingId()
    {
        var blogPost = SeedMapping("blogPost", "BlogPosting", propertyMappings:
        [
            new PropertyMapping { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "title" },
            new PropertyMapping { SchemaPropertyName = "author", SourceType = "static", StaticValue = "Jane Smith" },
        ]);
        var product = SeedMapping("product", "Product", propertyMappings:
        [
            new PropertyMapping { SchemaPropertyName = "name", SourceType = "property", ContentTypePropertyAlias = "productName" },
        ]);

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var blogProps = repository.GetPropertyMappings(blogPost.Id).ToList();
            var productProps = repository.GetPropertyMappings(product.Id).ToList();

            blogProps.Should().HaveCount(2);
            blogProps.Select(x => x.SchemaPropertyName)
                .Should().BeEquivalentTo(new[] { "headline", "author" });

            productProps.Should().HaveCount(1);
            productProps[0].SchemaPropertyName.Should().Be("name");
        }
    }

    [Fact]
    public void SavePropertyMappings_ReplacesExistingRowsForSchemaMapping()
    {
        var seeded = SeedMapping("blogPost", "BlogPosting", propertyMappings:
        [
            new PropertyMapping { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "title" },
            new PropertyMapping { SchemaPropertyName = "author", SourceType = "static", StaticValue = "Old Author" },
        ]);

        var replacement = new[]
        {
            new PropertyMapping { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "pageTitle" },
            new PropertyMapping { SchemaPropertyName = "datePublished", SourceType = "property", ContentTypePropertyAlias = "publishDate" },
            new PropertyMapping { SchemaPropertyName = "publisher", SourceType = "parent", ContentTypePropertyAlias = "organisationName" },
        };

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            repository.SavePropertyMappings(seeded.Id, replacement);

            var after = repository.GetPropertyMappings(seeded.Id).ToList();
            after.Should().HaveCount(3);
            after.Select(x => x.SchemaPropertyName)
                .Should().BeEquivalentTo(new[] { "headline", "datePublished", "publisher" });
            after.Should().NotContain(x => x.SchemaPropertyName == "author");
        }
    }

    [Fact]
    public void GetInheritedMappings_ReturnsOnlyEnabledInheritedRows()
    {
        SeedMapping("homePage", "WebSite", isInherited: true, isEnabled: true);
        SeedMapping("archivePage", "WebPage", isInherited: true, isEnabled: false);
        SeedMapping("blogPost", "BlogPosting", isInherited: false, isEnabled: true);
        SeedMapping("newsPage", "NewsArticle", isInherited: true, isEnabled: true);

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var inherited = repository.GetInheritedMappings().ToList();

            inherited.Should().HaveCount(2);
            inherited.Select(x => x.ContentTypeAlias)
                .Should().BeEquivalentTo(new[] { "homePage", "newsPage" });
        }
    }
}
