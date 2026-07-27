using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Composing;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class CoreAssemblyReferencesTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferenceUmbracoDeploy()
    {
        // The Deploy integration lives entirely in the optional
        // Umbraco.Community.SchemeWeaver.Deploy satellite. The core package must
        // stay installable (and this test project bootable) without any Deploy
        // assembly on disk — a compile-time firewall, like the uSync seams.
        typeof(SchemeWeaverComposer).Assembly.GetReferencedAssemblies()
            .Should().NotContain(a => a.Name!.StartsWith("Umbraco.Deploy"));
    }
}
