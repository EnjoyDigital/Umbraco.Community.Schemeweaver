using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Umbraco.Deploy.Core;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Integration.Fixtures;

/// <summary>
/// Boots the TestHost with the full Umbraco Deploy OnPrem runtime active — OnPrem
/// is in THIS test project's dependency context, so Umbraco's type scanner runs its
/// composer here (and only here).
/// </summary>
/// <remarks>
/// <para>
/// Deploy's extraction pipeline is gated on its licensing service. The tests swap
/// in <see cref="NullLicensing"/> — a public type Umbraco ships and documents as an
/// "'Always valid' license for temporary use in testing" — which makes the REAL
/// disk read/write pipeline fully exercisable in CI with no licence. This is a
/// test-only device; it is not a way to run Deploy unlicensed in production.
/// The <c>ILicensing</c> service interface is internal to Umbraco.Deploy.Core, so
/// the descriptor swap goes through reflection over NullLicensing's interface list.
/// </para>
/// <para>
/// Deploy maps its folders from the content root (the TestHost project directory)
/// and caches the state directory statically, so all factories in this process
/// share one <c>umbraco/Deploy</c> folder: Deploy integration tests must run in a
/// single serialised collection and clean the artifact folder between tests.
/// </para>
/// </remarks>
public class DeployWebApplicationFactory : SchemeWeaverWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var licensingInterface = typeof(NullLicensing).GetInterfaces()
                .Single(i => i.Name == "ILicensing");

            services.RemoveAll(licensingInterface);
            services.Add(ServiceDescriptor.Singleton(licensingInterface, new NullLicensing()));
        });
    }
}

[Xunit.CollectionDefinition(Name)]
public class SchemeWeaverDeployIntegrationCollection : Xunit.ICollectionFixture<DeployWebApplicationFactory>
{
    public const string Name = "SchemeWeaver Deploy Integration";
}
