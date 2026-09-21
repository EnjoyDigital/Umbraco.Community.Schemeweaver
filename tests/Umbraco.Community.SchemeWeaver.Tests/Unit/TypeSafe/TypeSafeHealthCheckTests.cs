using FluentAssertions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.HealthChecks;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="TypeSafeHealthCheck"/> against a fake client: the four states the backoffice
/// dashboard can show. The network is never touched.
/// </summary>
public class TypeSafeHealthCheckTests
{
    private static TypeSafeHealthCheck Create(ITypeSafeClient client, TypeSafeOptions options)
        => new(client, Options.Create(options), new RecordingLogger<TypeSafeHealthCheck>());

    private static async Task<HealthCheckStatus> StatusOf(TypeSafeHealthCheck check)
        => (await check.GetStatusAsync()).Should().ContainSingle().Subject;

    [Fact]
    public async Task Disabled_ReportsInfo()
    {
        var client = new FakeTypeSafeClient((_, _) => FakeTypeSafeClient.Noul(1));

        var status = await StatusOf(Create(client, new TypeSafeOptions { Enabled = false, ApiKey = "key" }));

        status.ResultType.Should().Be(StatusResultType.Info);
        status.Message.Should().Contain("disabled");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoApiKey_ReportsWarning()
    {
        var client = new FakeTypeSafeClient((_, _) => FakeTypeSafeClient.Noul(1));

        var status = await StatusOf(Create(client, new TypeSafeOptions { Enabled = true, ApiKey = " " }));

        status.ResultType.Should().Be(StatusResultType.Warning);
        status.Message.Should().Contain("ApiKey");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PingSucceeds_ReportsSuccessNamingTheModel()
    {
        var client = new FakeTypeSafeClient((_, _) => FakeTypeSafeClient.Noul(0.99));

        var status = await StatusOf(Create(client, new TypeSafeOptions { Enabled = true, ApiKey = "key" }));

        status.ResultType.Should().Be(StatusResultType.Success);
        status.Message.Should().Contain("fake-jev", "the model id the API reported is shown");
        status.Description.Should().Contain("0.99");
        client.Requests.Should().ContainSingle();
        client.Requests[0].State.Should().Be("ping");
        client.Requests[0].Questions.Should().ContainKey("ping").WhoseValue.Type.Should().Be("noul");
    }

    [Fact]
    public async Task PingThrowsApiException_ReportsErrorWithStatus()
    {
        var client = new FakeTypeSafeClient((_, _) => null) { Throws = new TypeSafeApiException("TypeSafe returned HTTP 503: down", 503) };

        var status = await StatusOf(Create(client, new TypeSafeOptions { Enabled = true, ApiKey = "key" }));

        status.ResultType.Should().Be(StatusResultType.Error);
        status.Message.Should().Contain("HTTP 503");
    }

    [Fact]
    public async Task PingThrowsAnythingElse_ReportsErrorInsteadOfThrowing()
    {
        var client = new FakeTypeSafeClient((_, _) => null) { Throws = new InvalidOperationException("boom") };

        var status = await StatusOf(Create(client, new TypeSafeOptions { Enabled = true, ApiKey = "key" }));

        status.ResultType.Should().Be(StatusResultType.Error);
        status.Message.Should().Contain("boom");
    }

    [Fact]
    public void ExecuteAction_HasNoActions()
    {
        var check = Create(new FakeTypeSafeClient((_, _) => null), new TypeSafeOptions());

        var status = check.ExecuteAction(new HealthCheckAction());

        status.ResultType.Should().Be(StatusResultType.Info);
    }
}
