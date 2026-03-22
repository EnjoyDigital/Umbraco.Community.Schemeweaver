using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaTypeRegistryTests
{
    private readonly SchemaTypeRegistry _sut = new();

    [Fact]
    public void GetAllTypes_ReturnsNonEmptyCollection()
    {
        var types = _sut.GetAllTypes();

        types.Should().NotBeEmpty();
    }

    [Fact]
    public void GetType_KnownType_ReturnsCorrectTypeInfo()
    {
        var result = _sut.GetType("Article");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Article");
    }

    [Fact]
    public void GetType_UnknownType_ReturnsNull()
    {
        var result = _sut.GetType("NonExistentSchemaType12345");

        result.Should().BeNull();
    }

    [Fact]
    public void GetProperties_KnownType_ReturnsProperties()
    {
        var properties = _sut.GetProperties("Article");

        properties.Should().NotBeEmpty();
    }

    [Fact]
    public void GetProperties_UnknownType_ReturnsEmpty()
    {
        var properties = _sut.GetProperties("NonExistentSchemaType12345");

        properties.Should().BeEmpty();
    }

    [Fact]
    public void Search_WithQuery_FiltersTypes()
    {
        var results = _sut.Search("Article").ToList();

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(t =>
            t.Name.Should().ContainEquivalentOf("Article"));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAllTypes()
    {
        var allTypes = _sut.GetAllTypes().ToList();
        var searchResults = _sut.Search("").ToList();

        searchResults.Should().HaveCount(allTypes.Count);
    }

    [Fact]
    public void GetClrType_KnownType_ReturnsType()
    {
        var clrType = _sut.GetClrType("Article");

        clrType.Should().NotBeNull();
        clrType!.Name.Should().Be("Article");
    }

    [Fact]
    public void GetClrType_UnknownType_ReturnsNull()
    {
        var clrType = _sut.GetClrType("NonExistentSchemaType12345");

        clrType.Should().BeNull();
    }

    [Fact]
    public void GetType_IsCaseInsensitive()
    {
        var upper = _sut.GetType("ARTICLE");
        var lower = _sut.GetType("article");

        upper.Should().NotBeNull();
        lower.Should().NotBeNull();
        upper!.Name.Should().Be(lower!.Name);
    }

    [Fact]
    public void GetProperties_ReturnsInheritedProperties()
    {
        var properties = _sut.GetProperties("Article").ToList();

        // Name comes from Thing, Headline from CreativeWork — both inherited
        properties.Should().Contain(p => p.Name == "Name");
        properties.Should().Contain(p => p.Name == "Headline");
    }

    [Fact]
    public void GetProperties_NoDuplicates()
    {
        var properties = _sut.GetProperties("Article").ToList();

        var duplicates = properties.GroupBy(p => p.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty("property names should be unique");
    }

    [Fact]
    public void GetProperties_UnionType_HasAcceptedTypes()
    {
        var properties = _sut.GetProperties("Article").ToList();
        var authorProp = properties.First(p => p.Name == "Author");

        authorProp.AcceptedTypes.Should().Contain("Organization");
        authorProp.AcceptedTypes.Should().Contain("Person");
    }

    [Fact]
    public void GetProperties_UnionType_IsComplexTrue()
    {
        var properties = _sut.GetProperties("Article").ToList();
        var authorProp = properties.First(p => p.Name == "Author");

        authorProp.IsComplexType.Should().BeTrue();
    }

    [Fact]
    public void GetProperties_SimpleString_IsComplexFalse()
    {
        var properties = _sut.GetProperties("Article").ToList();
        var headlineProp = properties.First(p => p.Name == "Headline");

        headlineProp.IsComplexType.Should().BeFalse();
    }

    [Fact]
    public void GetProperties_SimpleString_HasAcceptedTypes()
    {
        var properties = _sut.GetProperties("Article").ToList();
        var headlineProp = properties.First(p => p.Name == "Headline");

        headlineProp.AcceptedTypes.Should().NotBeEmpty();
        headlineProp.AcceptedTypes.Should().Contain("String");
    }

    [Fact]
    public void GetProperties_HowTo_ListsStepProperty()
    {
        var properties = _sut.GetProperties("HowTo").Select(p => p.Name).ToList();
        properties.Should().NotBeEmpty();

        // Verify exact step property name
        properties.Should().Contain("Step", "HowTo must have a property named 'Step'");
    }

    [Fact]
    public void GetProperties_HowTo_HasExpectedProperties()
    {
        var properties = _sut.GetProperties("HowTo").Select(p => p.Name).ToList();

        // These are the properties we map in the seeder
        properties.Should().Contain("Name");
        properties.Should().Contain("Description");
        properties.Should().Contain("TotalTime");
        properties.Should().Contain("EstimatedCost");
        properties.Should().Contain("Step");
        properties.Should().Contain("Tool");
    }

    [Theory]
    [InlineData("NewsArticle")]
    [InlineData("TechArticle")]
    [InlineData("HowTo")]
    [InlineData("ContactPage")]
    [InlineData("AboutPage")]
    [InlineData("CollectionPage")]
    [InlineData("ProfilePage")]
    [InlineData("WebSite")]
    [InlineData("WebPage")]
    [InlineData("VideoObject")]
    [InlineData("JobPosting")]
    [InlineData("Course")]
    [InlineData("SoftwareApplication")]
    [InlineData("Book")]
    [InlineData("Restaurant")]
    [InlineData("LocalBusiness")]
    public void GetType_ExpandedSchemaTypes_ExistsInRegistry(string typeName)
    {
        var result = _sut.GetType(typeName);

        result.Should().NotBeNull($"Schema.NET should contain type '{typeName}'");
        result!.Name.Should().Be(typeName);
    }
}
