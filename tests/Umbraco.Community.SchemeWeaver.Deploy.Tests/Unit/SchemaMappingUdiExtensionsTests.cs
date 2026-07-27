using FluentAssertions;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class SchemaMappingUdiExtensionsTests
{
    [Fact]
    public void GetUdi_UsesEntityTypeAndContentTypeKey_AndIsClosed()
    {
        var key = Guid.NewGuid();
        var mapping = ConnectorTestHarness.Mapping(key: key);

        var udi = mapping.GetUdi();

        udi.EntityType.Should().Be(SchemeWeaverDeployConstants.MappingUdiEntityType);
        udi.Guid.Should().Be(key);
        udi.IsRoot.Should().BeFalse();
    }
}
