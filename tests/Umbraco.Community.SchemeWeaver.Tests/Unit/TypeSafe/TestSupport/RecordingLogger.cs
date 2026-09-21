using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every entry (level, rendered message, structured
/// values, exception) so tests can assert on what was logged — the retry delay the client
/// chose, the Warning the decorator writes on fallback — without an NSubstitute
/// <c>Received()</c> over the generic <c>Log&lt;TState&gt;</c> signature.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
                values[key] = value;
        }

        Entries.Add(new Entry(logLevel, formatter(state, exception), values, exception));
    }
}
