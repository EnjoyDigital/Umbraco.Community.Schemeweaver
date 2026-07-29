namespace Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;

/// <summary>
/// Tracks whether the "Deploy runtime not installed" warning has been emitted, so
/// the satellite warns exactly once per process instead of on every save.
/// </summary>
/// <remarks>
/// The satellite only references <c>Umbraco.Deploy.Infrastructure</c>; the services
/// it needs (<c>IDiskEntityService</c> etc.) are registered by the separately
/// installed, licensed OnPrem/Cloud package. Installing the satellite without that
/// package must degrade to a warning — never crash the host site.
/// </remarks>
internal static class DeployRuntimeStatus
{
    private static int _warned;

    /// <summary>Returns <c>true</c> the first time it is called, <c>false</c> after.</summary>
    public static bool TryMarkWarned() => Interlocked.Exchange(ref _warned, 1) == 0;

    /// <summary>Test seam: resets the warn-once latch.</summary>
    internal static void Reset() => Interlocked.Exchange(ref _warned, 0);
}
