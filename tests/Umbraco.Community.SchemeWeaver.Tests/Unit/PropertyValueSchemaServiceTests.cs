using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Covers the Umbraco 17.4+ value-schema wrapper, including the graceful-degradation path when the
/// core <c>IPropertyEditorSchemaService</c> is unavailable (host predates 17.4).
/// </summary>
public class PropertyValueSchemaServiceTests
{
    private static PropertyValueSchemaService Create(IPropertyEditorSchemaService? schemaService)
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IPropertyEditorSchemaService)).Returns(schemaService);
        return new PropertyValueSchemaService(sp, NullLogger<PropertyValueSchemaService>.Instance);
    }

    [Fact]
    public async Task GetDataTypeValueSchemaAsync_ServiceUnavailable_NotAvailableAndReturnsNull()
    {
        var sut = Create(null);

        sut.IsAvailable.Should().BeFalse();
        (await sut.GetDataTypeValueSchemaAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetDataTypeValueSchemaAsync_Success_ReturnsSerialisedSchema()
    {
        var schemaService = Substitute.For<IPropertyEditorSchemaService>();
        var key = Guid.NewGuid();
        var json = new JsonObject { ["type"] = "string", ["maxLength"] = 250 };
        schemaService.GetSchemaAsync(key).Returns(
            Attempt.SucceedWithStatus(PropertyEditorSchemaOperationStatus.Success, new PropertyValueSchema(typeof(string), json)));

        var sut = Create(schemaService);

        sut.IsAvailable.Should().BeTrue();
        var result = await sut.GetDataTypeValueSchemaAsync(key);
        result.Should().Contain("\"maxLength\":250");
    }

    [Fact]
    public async Task GetDataTypeValueSchemaAsync_EditorWithoutSchemaProvider_ReturnsNull()
    {
        var schemaService = Substitute.For<IPropertyEditorSchemaService>();
        var key = Guid.NewGuid();
        schemaService.GetSchemaAsync(key).Returns(
            Attempt.FailWithStatus(PropertyEditorSchemaOperationStatus.SchemaNotSupported, new PropertyValueSchema(null, null)));

        var sut = Create(schemaService);

        (await sut.GetDataTypeValueSchemaAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task GetDataTypeValueSchemaAsync_EmptyKey_ReturnsNullWithoutCallingService()
    {
        var schemaService = Substitute.For<IPropertyEditorSchemaService>();
        var sut = Create(schemaService);

        (await sut.GetDataTypeValueSchemaAsync(Guid.Empty)).Should().BeNull();
        await schemaService.DidNotReceive().GetSchemaAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetDataTypeValueSchemaAsync_CalledTwice_CachesAndCallsServiceOnce()
    {
        var schemaService = Substitute.For<IPropertyEditorSchemaService>();
        var key = Guid.NewGuid();
        schemaService.GetSchemaAsync(key).Returns(
            Attempt.SucceedWithStatus(PropertyEditorSchemaOperationStatus.Success, new PropertyValueSchema(null, new JsonObject { ["type"] = "string" })));

        var sut = Create(schemaService);
        await sut.GetDataTypeValueSchemaAsync(key);
        await sut.GetDataTypeValueSchemaAsync(key);

        await schemaService.Received(1).GetSchemaAsync(key);
    }
}
