using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="SchemaTypeGraph"/> over the REAL <see cref="Services.SchemaTypeRegistry"/>: the
/// subtype relation must come from the Schema.NET interfaces, so multiply-inherited types
/// (HowToStep, Question) sit under every one of their supertypes and the synthesised
/// combining base classes never leak out as type names.
/// </summary>
public class SchemaTypeGraphTests
{
    private readonly SchemaTypeGraph _graph = SharedSchemaRegistry.Graph;

    [Fact]
    public void ChildrenOf_Thing_ContainsTheTopLevelFamilies()
    {
        var children = _graph.ChildrenOf("Thing");

        children.Should().Contain(["CreativeWork", "Person", "Place", "Organization", "Event", "Product"]);
        children.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("HowToStep", "CreativeWork")]
    [InlineData("Question", "Thing")]
    [InlineData("BedAndBreakfast", "LodgingBusiness")]
    [InlineData("BlogPosting", "Article")]
    [InlineData("BlogPosting", "BlogPosting")]
    public void IsSubtypeOf_TrueAcrossSingleAndMultipleInheritance(string candidate, string ancestor)
    {
        _graph.IsSubtypeOf(candidate, ancestor).Should().BeTrue();
    }

    [Fact]
    public void IsSubtypeOf_UnrelatedTypes_IsFalse()
    {
        _graph.IsSubtypeOf("Person", "CreativeWork").Should().BeFalse();
        _graph.IsSubtypeOf("CreativeWork", "BlogPosting").Should().BeFalse("the relation is directional");
    }

    [Fact]
    public void IsSubtypeOf_IsCaseInsensitive()
    {
        _graph.IsSubtypeOf("blogposting", "ARTICLE").Should().BeTrue();
        _graph.IsSubtypeOf("howtostep", "creativework").Should().BeTrue();
    }

    [Fact]
    public void ParentsOf_HowToStep_FlattensTheCombiningBaseClass()
    {
        // Schema.NET declares HowToStep : CreativeWorkAndItemListAndListItem; that synthesised
        // class is not a Schema.org type and must never appear.
        var parents = _graph.ParentsOf("HowToStep");

        parents.Should().Contain("CreativeWork");
        parents.Should().NotContain(p => p.Contains("And", StringComparison.Ordinal));
        parents.Should().OnlyContain(p => SharedSchemaRegistry.Registry.GetType(p) != null, "every parent is a registry type");
    }

    [Fact]
    public void ChildrenOf_IncludesMultiplyInheritedTypesUnderEachParent()
    {
        _graph.ChildrenOf("CreativeWork").Should().Contain("HowToStep");
        _graph.ChildrenOf("ItemList").Should().Contain("HowToStep");
        _graph.ChildrenOf("Organization").Should().Contain("LocalBusiness");
        _graph.ChildrenOf("Place").Should().Contain("LocalBusiness");
    }

    [Fact]
    public void RangeOf_BlogPostingAuthor_IsExactlyOrganizationAndPerson()
    {
        _graph.RangeOf("BlogPosting", "Author").Should().BeEquivalentTo(["Organization", "Person"]);
    }

    [Fact]
    public void RangeOf_RecipeInstructions_ContainsEntityTypesAndNoPrimitives()
    {
        var range = _graph.RangeOf("Recipe", "RecipeInstructions");

        range.Should().Contain(["CreativeWork", "ItemList"]);
        range.Should().NotContain(["Text", "URL", "Number", "DateTime", "Boolean"]);
    }

    [Fact]
    public void RangeOf_IsCaseInsensitiveOnTypeAndProperty()
    {
        _graph.RangeOf("blogposting", "author").Should().BeEquivalentTo(["Organization", "Person"]);
    }

    [Fact]
    public void UnknownTypeOrProperty_GivesEmptyOrFalse()
    {
        _graph.ChildrenOf("NotAType").Should().BeEmpty();
        _graph.ParentsOf("NotAType").Should().BeEmpty();
        _graph.RangeOf("NotAType", "Name").Should().BeEmpty();
        _graph.RangeOf("BlogPosting", "notAProperty").Should().BeEmpty();
        _graph.IsSubtypeOf("NotAType", "Thing").Should().BeFalse();
        _graph.IsSubtypeOf("BlogPosting", "NotAType").Should().BeFalse();
        _graph.IsSubtypeOf("", "Thing").Should().BeFalse();
    }
}
