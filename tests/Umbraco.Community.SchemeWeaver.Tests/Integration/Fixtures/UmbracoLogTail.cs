using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Reads recent errors out of the test host's Serilog file so a failing assertion can
/// report the exception the SERVER threw, not just the status code the client saw.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a concrete debugging dead end. The integration flake surfaced
/// only as "Expected OK but found InternalServerError" — <c>HandlesServerErrorAttribute</c>
/// deliberately returns a frozen, information-free body, so the assertion carried nothing
/// actionable. Locally the real exception was recoverable from the host's log file; on CI
/// that file is never uploaded, so the failure was undiagnosable from the run alone. Any
/// test that hits this again should now print the cause.
/// </para>
/// <para>
/// Umbraco writes <c>UmbracoTraceLog.*.json</c> in CLEF, one JSON object per line, using
/// <c>@t</c> (timestamp), <c>@l</c> (level) and <c>@x</c> (exception) — NOT
/// <c>Level</c>/<c>Exception</c>. Filtering on the wrong names returns zero matches and
/// looks exactly like "there were no errors", which is its own trap.
/// </para>
/// <para>
/// The log directory lives under the TestHost content root and is shared by every host in
/// the process, so results are time-windowed rather than attributed to one factory.
/// Best-effort by design: diagnostics must never themselves fail a test, so everything is
/// swallowed and the worst case is an empty string.
/// </para>
/// </remarks>
// Public rather than internal: the Deploy test project is a separate assembly and needs it.
public static class UmbracoLogTail
{
    /// <summary>
    /// Returns the distinct exceptions logged at Error/Fatal within <paramref name="window"/>,
    /// or an empty string if none are found (or anything at all goes wrong).
    /// </summary>
    public static string RecentServerErrors(TimeSpan window, int maxErrors = 3, int maxLinesEach = 12)
    {
        try
        {
            var logDirectory = FindLogDirectory();
            if (logDirectory is null)
            {
                return string.Empty;
            }

            var newest = new DirectoryInfo(logDirectory)
                .EnumerateFiles("UmbracoTraceLog*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
            {
                return string.Empty;
            }

            var cutoff = DateTimeOffset.UtcNow - window;
            var found = new List<string>();

            // The host still has the file open, hence the sharing flags.
            using var stream = new FileStream(
                newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || !line.Contains("\"@x\"", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    var entry = JsonDocument.Parse(line).RootElement;

                    if (!entry.TryGetProperty("@l", out var level) ||
                        (level.GetString() is not "Error" and not "Fatal"))
                    {
                        continue;
                    }

                    if (entry.TryGetProperty("@t", out var timestamp) &&
                        DateTimeOffset.TryParse(timestamp.GetString(), out var when) &&
                        when < cutoff)
                    {
                        continue;
                    }

                    if (!entry.TryGetProperty("@x", out var exception))
                    {
                        continue;
                    }

                    var text = string.Join(
                        Environment.NewLine,
                        (exception.GetString() ?? string.Empty)
                            .Split('\n')
                            .Take(maxLinesEach)
                            .Select(l => l.TrimEnd('\r')));

                    if (!found.Contains(text))
                    {
                        found.Add(text);
                    }
                }
                catch (JsonException)
                {
                    // A partially flushed final line — skip it.
                }
            }

            if (found.Count == 0)
            {
                return string.Empty;
            }

            return Environment.NewLine +
                string.Join(
                    Environment.NewLine + "  --- and ---" + Environment.NewLine,
                    found.TakeLast(maxErrors));
        }
        catch (Exception)
        {
            // Diagnostics must never fail a test.
            return string.Empty;
        }
    }

    /// <summary>
    /// Walks up from the test binaries to the TestHost project's <c>umbraco/Logs</c>.
    /// </summary>
    private static string? FindLogDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Join(
                directory.FullName,
                "src",
                "Umbraco.Community.SchemeWeaver.TestHost",
                "umbraco",
                "Logs");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
