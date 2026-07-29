using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.ContentTypeEditing;
using Umbraco.Cms.Core.Services.ContentTypeEditing;
using Umbraco.Community.SchemeWeaver.Deploy.Connectors;
using Umbraco.Community.SchemeWeaver.Deploy.Tests.Integration.Fixtures;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Umbraco.Deploy.Core.Connectors.ServiceConnectors;
using Umbraco.Deploy.Infrastructure.Disk;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Integration;

/// <summary>
/// The license-free closed loop: a mapping saved through the real management API is
/// written to <c>umbraco/Deploy/Revision</c> by the REAL Deploy disk pipeline, the
/// database is wiped, and the REAL Deploy disk-read pipeline
/// (<see cref="IDiskEntityService.ProcessDiskReadAsync"/> — the same code the
/// <c>deploy</c> marker file and dashboard trigger) recreates the rows from the
/// .uda file. Real code at both ends; only the trigger is invoked directly.
/// </summary>
[Collection(SchemeWeaverDeployIntegrationCollection.Name)]
public class DeployDiskRoundTripTests : UmbracoIntegrationTestBase
{
    private const string BaseRoute = "/umbraco/management/api/v1/schemeweaver";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DeployDiskRoundTripTests(DeployWebApplicationFactory factory)
        : base(factory)
    {
    }

    // ----- plumbing -----

    private IDiskEntityService DiskEntityService
        => Factory.Services.GetRequiredService<IDiskEntityService>();

    private string ArtifactDirectory => DiskEntityService.GetArtifactDirectory();

    private void CleanArtifactDirectory()
    {
        foreach (var file in Directory.EnumerateFiles(ArtifactDirectory, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    private async Task<Guid> CreateContentTypeAsync(string alias)
    {
        using var scope = CreateServiceScope();
        var editingService = scope.ServiceProvider.GetRequiredService<IContentTypeEditingService>();
        var attempt = await editingService.CreateAsync(
            new ContentTypeCreateModel { Alias = alias, Name = alias, Icon = "icon-document" },
            Constants.Security.SuperUserKey);

        attempt.Success.Should().BeTrue($"content type '{alias}' should be creatable ({attempt.Status})");
        return attempt.Result!.Key;
    }

    private async Task SaveMappingAsync(string alias, Guid contentTypeKey)
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = alias,
            ContentTypeKey = contentTypeKey,
            SchemaTypeName = "BlogPosting",
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
    }

    private string UdaPath(Guid contentTypeKey)
        => Path.Join(ArtifactDirectory, $"schemeweaver-mapping__{contentTypeKey:N}.uda");

    /// <summary>
    /// The .uda write happens on the saved-notification path of the request we just
    /// awaited, but disk IO may complete marginally after the response — poll briefly.
    /// </summary>
    private async Task<string> WaitForUdaAsync(Guid contentTypeKey, bool expectExists = true, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var matches = Directory.Exists(ArtifactDirectory)
                ? Directory.EnumerateFiles(ArtifactDirectory, "*.uda", SearchOption.AllDirectories)
                    .Where(f => f.Contains(contentTypeKey.ToString("N"), StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(f).StartsWith("schemeweaver-mapping", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [];

            if (matches.Count > 0 == expectExists)
            {
                return matches.FirstOrDefault() ?? string.Empty;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Expected schemeweaver-mapping .uda for {contentTypeKey} to {(expectExists ? "appear" : "disappear")} in {ArtifactDirectory} within {timeoutMs}ms.");
    }

    /// <summary>
    /// Simulates a fresh target environment. Wiping the mapping tables alone is not
    /// enough: the save-time refresher also stored Deploy signatures, and the disk
    /// read's manifest review skips any artifact whose checksum matches its stored
    /// signature ("up to date"). A real fresh target has no signatures either.
    /// </summary>
    private void ResetTargetState()
    {
        ResetSchemeWeaverTables();
        Factory.Services.GetRequiredService<Umbraco.Deploy.Core.ISignatureService>().ClearSignatures();
    }

    private async Task RunDiskReadAsync()
    {
        var diskEntityService = DiskEntityService;
        var statePath = diskEntityService.GetStateDirectory();
        var completeMarker = Path.Join(statePath, "deploy-complete");
        var failedMarker = Path.Join(statePath, "deploy-failed");

        // Deploy's work runner silently skips when another work item holds the
        // environment worker (it logs "another deploy is in flight" and returns
        // without writing any marker) — on a fast CI runner the previous test's
        // work can still be draining. Treat silence as "not run" and retry;
        // only a written deploy-complete marker counts as success.
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            File.Delete(completeMarker);
            await diskEntityService.ProcessDiskReadAsync(
                Guid.NewGuid(), statePath, diskEntityService.GetDiskReadEventTrigger());

            if (File.Exists(failedMarker))
            {
                throw new InvalidOperationException(
                    $"Deploy disk read failed: {await File.ReadAllTextAsync(failedMarker)}");
            }

            if (File.Exists(completeMarker))
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            "Deploy disk read never ran to completion (no deploy-complete marker written after 20 attempts).");
    }

    // ----- tests (serialised: same class + shared collection fixture) -----

    [Fact]
    public void DeployRuntime_IsActive_AndConnectorRegistered()
    {
        // OnPrem's composer ran (it is in this project's dependency context only),
        // so the disk service resolves...
        var diskEntityService = Factory.Services.GetService<IDiskEntityService>();
        diskEntityService.Should().NotBeNull(
            "Umbraco.Deploy.OnPrem is in this test project's dependency context");

        // ...our connector was type-scanned into the connector collection...
        var connector = Factory.Services.GetRequiredService<IServiceConnectorFactory>()
            .GetConnector(SchemeWeaverDeployConstants.MappingUdiEntityType);
        connector.Should().BeOfType<SchemaMappingServiceConnector>();

        // ...and the startup handler already registered the disk entity type
        // (a second registration reports "not added").
        diskEntityService!.RegisterDiskEntityType(SchemeWeaverDeployConstants.MappingUdiEntityType)
            .Should().BeFalse("the startup handler registered the entity type at boot");
    }

    [Fact]
    public async Task SaveMapping_WritesUdaToRevisionFolder_WithDependencyAndFields()
    {
        CleanArtifactDirectory();
        var key = await CreateContentTypeAsync($"deployWrite{Guid.NewGuid():N}"[..24]);
        var alias = await GetContentTypeAliasAsync(key);

        await SaveMappingAsync(alias, key);

        var udaPath = await WaitForUdaAsync(key);
        var json = JsonDocument.Parse(await File.ReadAllTextAsync(udaPath));

        json.RootElement.GetProperty("__type").GetString().Should()
            .Contain("Umbraco.Community.SchemeWeaver.Deploy");
        json.RootElement.GetProperty("Udi").GetString().Should()
            .Be($"umb://schemeweaver-mapping/{key:N}");
        json.RootElement.GetProperty("ContentTypeAlias").GetString().Should().Be(alias);
        json.RootElement.GetProperty("SchemaTypeName").GetString().Should().Be("BlogPosting");
        json.RootElement.GetProperty("PropertyMappings").GetArrayLength().Should().Be(2);
        json.RootElement.GetProperty("Dependencies").EnumerateArray()
            .Select(d => d.GetProperty("Udi").GetString())
            .Should().Contain($"umb://document-type/{key:N}");
    }

    [Fact]
    public async Task DeleteMapping_RemovesUda()
    {
        CleanArtifactDirectory();
        var key = await CreateContentTypeAsync($"deployDelete{Guid.NewGuid():N}"[..24]);
        var alias = await GetContentTypeAliasAsync(key);
        await SaveMappingAsync(alias, key);
        await WaitForUdaAsync(key);

        var response = await Client.DeleteAsync($"{BaseRoute}/mappings/{alias}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await WaitForUdaAsync(key, expectExists: false);
    }

    [Fact]
    public async Task Roundtrip_UdaToDatabase_RecreatesMappingViaRealDiskRead()
    {
        CleanArtifactDirectory();
        var key = await CreateContentTypeAsync($"deployLoop{Guid.NewGuid():N}"[..24]);
        var alias = await GetContentTypeAliasAsync(key);
        await SaveMappingAsync(alias, key);
        var udaPath = await WaitForUdaAsync(key);
        var udaBytesBefore = await File.ReadAllBytesAsync(udaPath);

        // Wipe the SchemeWeaver tables and Deploy signatures — the .uda in the
        // revision folder is now the only place the mapping exists, exactly like a
        // fresh target environment.
        ResetTargetState();

        await RunDiskReadAsync();

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var restored = repository.GetByContentTypeAlias(alias);
            restored.Should().NotBeNull("the Deploy disk read should have recreated the mapping row");
            restored!.ContentTypeKey.Should().Be(key);
            restored.SchemaTypeName.Should().Be("BlogPosting");
            restored.IsEnabled.Should().BeTrue();

            var rows = repository.GetPropertyMappings(restored.Id).ToList();
            rows.Select(r => r.SchemaPropertyName).Should().ContainInOrder("headline", "author");
            rows.Single(r => r.SchemaPropertyName == "author").StaticValue.Should().Be("Jane Smith");
        }

        // Loop-freedom, system level: extraction writes via the repository, which
        // publishes no notifications, so the refresher must NOT have re-written the
        // artifact during the disk read.
        (await File.ReadAllBytesAsync(udaPath)).Should().Equal(udaBytesBefore);
    }

    [Fact]
    public async Task Reextraction_IsIdempotent()
    {
        CleanArtifactDirectory();
        var key = await CreateContentTypeAsync($"deployIdem{Guid.NewGuid():N}"[..24]);
        var alias = await GetContentTypeAliasAsync(key);
        await SaveMappingAsync(alias, key);
        await WaitForUdaAsync(key);

        ResetTargetState();
        await RunDiskReadAsync();
        await RunDiskReadAsync();

        var (repository, scope) = CreateRepository();
        using (scope)
        {
            repository.GetAll().Where(m => m.ContentTypeAlias == alias).Should().ContainSingle();
            var mapping = repository.GetByContentTypeAlias(alias)!;
            repository.GetPropertyMappings(mapping.Id).Should().HaveCount(2);
        }
    }

    private async Task<string> GetContentTypeAliasAsync(Guid key)
    {
        using var scope = CreateServiceScope();
        var contentTypeService = scope.ServiceProvider
            .GetRequiredService<Umbraco.Cms.Core.Services.IContentTypeService>();
        var contentType = contentTypeService.Get(key);
        contentType.Should().NotBeNull();
        await Task.CompletedTask;
        return contentType!.Alias;
    }
}
