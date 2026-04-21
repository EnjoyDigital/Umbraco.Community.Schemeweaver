using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.uSync;
using uSync.Core.Models;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaMappingSerializerTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly ILogger<SchemaMappingSerializer> _logger = Substitute.For<ILogger<SchemaMappingSerializer>>();
    private readonly SchemaMappingSerializer _sut;

    public SchemaMappingSerializerTests()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(_repository);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _sut = new SchemaMappingSerializer(scopeFactory, _logger);
    }

    private static SchemaMapping CreateTestMapping(bool isInherited = false) => new()
    {
        Id = 1,
        ContentTypeAlias = "blogPost",
        ContentTypeKey = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
        SchemaTypeName = "BlogPosting",
        IsEnabled = true,
        IsInherited = isInherited,
        CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
    };

    private static List<PropertyMapping> CreateTestPropertyMappings(int schemaMappingId = 1) =>
    [
        new()
        {
            Id = 10,
            SchemaMappingId = schemaMappingId,
            SchemaPropertyName = "headline",
            SourceType = "property",
            ContentTypePropertyAlias = "title",
            IsAutoMapped = true,
        },
        new()
        {
            Id = 11,
            SchemaMappingId = schemaMappingId,
            SchemaPropertyName = "author",
            SourceType = "static",
            StaticValue = "Jane Smith",
            IsAutoMapped = false,
            DynamicRootConfig = "{\"originAlias\":\"Root\",\"querySteps\":[{\"unique\":\"guid-123\",\"alias\":\"child\"}]}",
        },
        new()
        {
            Id = 12,
            SchemaMappingId = schemaMappingId,
            SchemaPropertyName = "mainEntity",
            SourceType = "blockContent",
            ContentTypePropertyAlias = "faqItems",
            NestedSchemaTypeName = "Question",
            ResolverConfig = "{\"mappings\":[{\"schemaProperty\":\"name\",\"blockProperty\":\"question\"}]}",
            IsAutoMapped = false,
        },
    ];

    [Fact]
    public async Task Serialize_IncludesIsInherited_WhenTrue()
    {
        var mapping = CreateTestMapping(isInherited: true);
        _repository.GetPropertyMappings(mapping.Id).Returns(new List<PropertyMapping>());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var info = result.Item!.Element("Info");
        info.Should().NotBeNull();
        info!.Element("IsInherited")!.Value.Should().BeOneOf("True", "true");
    }

    [Fact]
    public async Task Serialize_IncludesIsInherited_WhenFalse()
    {
        var mapping = CreateTestMapping(isInherited: false);
        _repository.GetPropertyMappings(mapping.Id).Returns(new List<PropertyMapping>());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var info = result.Item!.Element("Info");
        info!.Element("IsInherited")!.Value.Should().BeOneOf("False", "false");
    }

    [Fact]
    public async Task Serialize_IncludesAllInfoFields()
    {
        var mapping = CreateTestMapping();
        _repository.GetPropertyMappings(mapping.Id).Returns(new List<PropertyMapping>());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var info = result.Item!.Element("Info")!;
        info.Element("ContentTypeAlias")!.Value.Should().Be("blogPost");
        info.Element("ContentTypeKey")!.Value.Should().Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        info.Element("SchemaTypeName")!.Value.Should().Be("BlogPosting");
        info.Element("IsEnabled")!.Value.Should().BeOneOf("True", "true");
        info.Element("IsInherited")!.Value.Should().BeOneOf("False", "false");
    }

    [Fact]
    public async Task Serialize_IncludesPropertyMappings()
    {
        var mapping = CreateTestMapping();
        _repository.GetPropertyMappings(mapping.Id).Returns(CreateTestPropertyMappings());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var pmNodes = result.Item!.Element("PropertyMappings")!.Elements("PropertyMapping").ToList();
        pmNodes.Should().HaveCount(3);

        // First: simple property mapping
        pmNodes[0].Element("SchemaPropertyName")!.Value.Should().Be("headline");
        pmNodes[0].Element("SourceType")!.Value.Should().Be("property");
        pmNodes[0].Element("ContentTypePropertyAlias")!.Value.Should().Be("title");
        pmNodes[0].Element("IsAutoMapped")!.Value.Should().BeOneOf("True", "true");

        // Second: static value mapping
        pmNodes[1].Element("StaticValue")!.Value.Should().Be("Jane Smith");

        // Third: block content with ResolverConfig in CDATA
        pmNodes[2].Element("NestedSchemaTypeName")!.Value.Should().Be("Question");
        pmNodes[2].Element("ResolverConfig")!.Value.Should().Contain("mappings");
    }

    [Fact]
    public async Task Serialize_OmitsNullOptionalFields()
    {
        var mapping = CreateTestMapping();
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 10,
                SchemaMappingId = 1,
                SchemaPropertyName = "name",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
                IsAutoMapped = true,
                // All optional fields are null
            }
        };
        _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

        var result = await InvokeSerializeCoreAsync(mapping);

        var pmNode = result.Item!.Element("PropertyMappings")!.Element("PropertyMapping")!;
        pmNode.Element("SourceContentTypeAlias").Should().BeNull();
        pmNode.Element("TransformType").Should().BeNull();
        pmNode.Element("StaticValue").Should().BeNull();
        pmNode.Element("NestedSchemaTypeName").Should().BeNull();
        pmNode.Element("ResolverConfig").Should().BeNull();
    }

    [Fact]
    public async Task Deserialize_ReadsIsInherited_WhenPresent()
    {
        var xml = CreateTestXml(isInherited: true);
        _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

        var result = await InvokeDeserializeCoreAsync(xml);

        result.Success.Should().BeTrue();
        result.Item!.IsInherited.Should().BeTrue();
    }

    [Fact]
    public async Task Deserialize_DefaultsIsInheritedToFalse_WhenMissing()
    {
        // XML without IsInherited element (backwards compatibility)
        var xml = CreateTestXml(includeIsInherited: false);
        _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

        var result = await InvokeDeserializeCoreAsync(xml);

        result.Success.Should().BeTrue();
        result.Item!.IsInherited.Should().BeFalse();
    }

    [Fact]
    public async Task Deserialize_ReadsAllPropertyMappingFields()
    {
        var xml = CreateTestXml();
        _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

        var savedMappings = new List<PropertyMapping>();
        _repository.When(r => r.SavePropertyMappings(Arg.Any<int>(), Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => savedMappings.AddRange(c.Arg<IEnumerable<PropertyMapping>>()));

        await InvokeDeserializeCoreAsync(xml);

        savedMappings.Should().HaveCount(2);
        savedMappings[0].SchemaPropertyName.Should().Be("headline");
        savedMappings[0].SourceType.Should().Be("property");
        savedMappings[0].ContentTypePropertyAlias.Should().Be("title");
        savedMappings[0].IsAutoMapped.Should().BeTrue();
        savedMappings[1].SchemaPropertyName.Should().Be("mainEntity");
        savedMappings[1].ResolverConfig.Should().Contain("mappings");
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var mapping = CreateTestMapping(isInherited: true);
        var propertyMappings = CreateTestPropertyMappings();
        _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

        // Serialize
        var serializeResult = await InvokeSerializeCoreAsync(mapping);
        serializeResult.Success.Should().BeTrue();

        // Deserialize from the serialized XML
        _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

        var savedMappings = new List<PropertyMapping>();
        _repository.When(r => r.SavePropertyMappings(Arg.Any<int>(), Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => savedMappings.AddRange(c.Arg<IEnumerable<PropertyMapping>>()));

        var deserializeResult = await InvokeDeserializeCoreAsync(serializeResult.Item!);

        // Verify round-trip fidelity
        deserializeResult.Success.Should().BeTrue();
        var roundTripped = deserializeResult.Item!;
        roundTripped.ContentTypeAlias.Should().Be("blogPost");
        roundTripped.ContentTypeKey.Should().Be(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
        roundTripped.SchemaTypeName.Should().Be("BlogPosting");
        roundTripped.IsEnabled.Should().BeTrue();
        roundTripped.IsInherited.Should().BeTrue();

        savedMappings.Should().HaveCount(3);
        savedMappings[2].NestedSchemaTypeName.Should().Be("Question");
        savedMappings[2].ResolverConfig.Should().Contain("mappings");
        savedMappings.Should().Contain(p =>
            p.DynamicRootConfig == "{\"originAlias\":\"Root\",\"querySteps\":[{\"unique\":\"guid-123\",\"alias\":\"child\"}]}");
    }

    [Fact]
    public async Task Serialize_IncludesIdOverride_WhenSet()
    {
        var mapping = CreateTestMapping();
        mapping.IdOverride = "{siteUrl}#organization";
        _repository.GetPropertyMappings(mapping.Id).Returns(new List<PropertyMapping>());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var info = result.Item!.Element("Info")!;
        info.Element("IdOverride")!.Value.Should().Be("{siteUrl}#organization");
    }

    [Fact]
    public async Task Serialize_OmitsIdOverride_WhenNull()
    {
        var mapping = CreateTestMapping();
        mapping.IdOverride = null;
        _repository.GetPropertyMappings(mapping.Id).Returns(new List<PropertyMapping>());

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var info = result.Item!.Element("Info")!;
        info.Element("IdOverride").Should().BeNull();
    }

    [Fact]
    public async Task Serialize_IncludesTargetPieceKey_WhenSet()
    {
        var mapping = CreateTestMapping();
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 30,
                SchemaMappingId = mapping.Id,
                SchemaPropertyName = "publisher",
                SourceType = "reference",
                TargetPieceKey = "organization",
                IsAutoMapped = false,
            }
        };
        _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

        var result = await InvokeSerializeCoreAsync(mapping);

        result.Success.Should().BeTrue();
        var pmNode = result.Item!.Element("PropertyMappings")!.Element("PropertyMapping")!;
        pmNode.Element("TargetPieceKey")!.Value.Should().Be("organization");
        pmNode.Element("SourceType")!.Value.Should().Be("reference");
    }

    [Fact]
    public async Task RoundTrip_PreservesIdOverrideAndTargetPieceKey()
    {
        // Guards against v1.4 fields (IdOverride, TargetPieceKey) being
        // forgotten in either the serialize or deserialize path — if either
        // drops the field, the uSync file will silently lose the setting
        // when it moves between environments.
        var mapping = CreateTestMapping();
        mapping.IdOverride = "{url}#{type}-{culture}";
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 40,
                SchemaMappingId = mapping.Id,
                SchemaPropertyName = "publisher",
                SourceType = "reference",
                TargetPieceKey = "organization",
                IsAutoMapped = false,
            },
            new()
            {
                Id = 41,
                SchemaMappingId = mapping.Id,
                SchemaPropertyName = "isPartOf",
                SourceType = "reference",
                TargetPieceKey = "website",
                IsAutoMapped = false,
            },
        };
        _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

        var serializeResult = await InvokeSerializeCoreAsync(mapping);
        serializeResult.Success.Should().BeTrue();

        _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

        var savedMappings = new List<PropertyMapping>();
        _repository.When(r => r.SavePropertyMappings(Arg.Any<int>(), Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => savedMappings.AddRange(c.Arg<IEnumerable<PropertyMapping>>()));

        var deserializeResult = await InvokeDeserializeCoreAsync(serializeResult.Item!);

        deserializeResult.Success.Should().BeTrue();
        deserializeResult.Item!.IdOverride.Should().Be("{url}#{type}-{culture}");

        savedMappings.Should().HaveCount(2);
        savedMappings[0].TargetPieceKey.Should().Be("organization");
        savedMappings[0].SourceType.Should().Be("reference");
        savedMappings[1].TargetPieceKey.Should().Be("website");
    }

    [Fact]
    public async Task Serialize_IncludesDynamicRootConfig_WhenSet()
    {
        const string dynamicRootJson = "{\"originAlias\":\"Root\"}";

        var mapping = CreateTestMapping();
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 20,
                SchemaMappingId = mapping.Id,
                SchemaPropertyName = "publisher",
                SourceType = "parent",
                SourceContentTypeAlias = "organization",
                IsAutoMapped = false,
                DynamicRootConfig = dynamicRootJson,
            }
        };
        _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

        var serializeResult = await InvokeSerializeCoreAsync(mapping);

        serializeResult.Success.Should().BeTrue();
        var pmNode = serializeResult.Item!.Element("PropertyMappings")!.Element("PropertyMapping")!;
        var dynamicRootElement = pmNode.Element("DynamicRootConfig");
        dynamicRootElement.Should().NotBeNull();
        dynamicRootElement!.Value.Should().Be(dynamicRootJson);

        var xmlString = serializeResult.Item!.ToString();
        xmlString.Should().Contain("<DynamicRootConfig");
        xmlString.Should().Contain(dynamicRootJson);
    }

    // === Helper methods ===

    /// <summary>
    /// Invokes the protected SerializeCoreAsync via the public interface.
    /// The serializer base class exposes Serialize publicly which calls SerializeCoreAsync.
    /// </summary>
    private Task<SyncAttempt<XElement>> InvokeSerializeCoreAsync(SchemaMapping item)
    {
        // Use reflection to call the protected method
        var method = typeof(SchemaMappingSerializer)
            .GetMethod("SerializeCoreAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task<SyncAttempt<XElement>>)method!.Invoke(_sut, [item, new SyncSerializerOptions()])!;
    }

    private Task<SyncAttempt<SchemaMapping>> InvokeDeserializeCoreAsync(XElement node)
    {
        var method = typeof(SchemaMappingSerializer)
            .GetMethod("DeserializeCoreAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task<SyncAttempt<SchemaMapping>>)method!.Invoke(_sut, [node, new SyncSerializerOptions()])!;
    }

    private static XElement CreateTestXml(bool isInherited = false, bool includeIsInherited = true)
    {
        var info = new XElement("Info",
            new XElement("ContentTypeAlias", "blogPost"),
            new XElement("ContentTypeKey", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            new XElement("SchemaTypeName", "BlogPosting"),
            new XElement("IsEnabled", true));

        if (includeIsInherited)
            info.Add(new XElement("IsInherited", isInherited));

        var propertyMappings = new XElement("PropertyMappings",
            new XElement("PropertyMapping",
                new XElement("SchemaPropertyName", "headline"),
                new XElement("SourceType", "property"),
                new XElement("ContentTypePropertyAlias", "title"),
                new XElement("IsAutoMapped", true)),
            new XElement("PropertyMapping",
                new XElement("SchemaPropertyName", "mainEntity"),
                new XElement("SourceType", "blockContent"),
                new XElement("ContentTypePropertyAlias", "faqItems"),
                new XElement("NestedSchemaTypeName", "Question"),
                new XElement("ResolverConfig", new XCData("{\"mappings\":[]}")),
                new XElement("IsAutoMapped", false)));

        return new XElement("SchemeWeaverMapping",
            new XAttribute("Key", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            new XAttribute("Alias", "blogPost"),
            info,
            propertyMappings);
    }
}
