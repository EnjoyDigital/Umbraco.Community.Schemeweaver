using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class PropertyMappingTests
{
    [Fact]
    public void DefaultSourceType_IsProperty()
    {
        var mapping = new PropertyMapping();

        mapping.SourceType.Should().Be("property");
    }

    [Fact]
    public void StaticSourceType_HasStaticValue()
    {
        var mapping = new PropertyMapping
        {
            SourceType = "static",
            StaticValue = "en-GB"
        };

        mapping.SourceType.Should().Be("static");
        mapping.StaticValue.Should().Be("en-GB");
    }

    [Fact]
    public void TransformType_CanBeSet()
    {
        var mapping = new PropertyMapping
        {
            TransformType = "stripHtml"
        };

        mapping.TransformType.Should().Be("stripHtml");
    }

    [Fact]
    public void NestedSchemaTypeName_CanBeSet()
    {
        var mapping = new PropertyMapping
        {
            NestedSchemaTypeName = "Person"
        };

        mapping.NestedSchemaTypeName.Should().Be("Person");
    }

    [Fact]
    public void NullableFields_DefaultToNull()
    {
        var mapping = new PropertyMapping();

        mapping.ContentTypePropertyAlias.Should().BeNull();
        mapping.SourceContentTypeAlias.Should().BeNull();
        mapping.TransformType.Should().BeNull();
        mapping.StaticValue.Should().BeNull();
        mapping.NestedSchemaTypeName.Should().BeNull();
    }
}
