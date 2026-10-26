using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ResortWise.LoanBalance.UnitTests.Infrastructure;

/// <summary>Captures log entries, including their structured state, for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(string Category, string Message, IReadOnlyDictionary<string, object?> State);

    private readonly ConcurrentQueue<Entry> entries = new();

    public IReadOnlyList<Entry> Entries => [.. entries];

    public IReadOnlyList<Entry> AuditEntries =>
        Entries.Where(entry => entry.Category.EndsWith(".AuditMiddleware", StringComparison.Ordinal)).ToList();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void Dispose()
    {
    }

    /// <summary>The message and every state value, as one string, for "never contains" checks.</summary>
    public static string Render(Entry entry) =>
        entry.Message + " " + string.Join(" ", entry.State.Select(pair => $"{pair.Key}={pair.Value}"));

    private sealed class CapturingLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            entries.Enqueue(new Entry(category, formatter(state, exception), values));
        }
    }
}

/// <summary>A typed logger over a capturing provider, for testing middleware directly.</summary>
public sealed class CapturingLogger<T>(CapturingLoggerProvider provider) : ILogger<T>
{
    private readonly ILogger inner = provider.CreateLogger(typeof(T).FullName!);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        inner.Log(logLevel, eventId, state, exception, formatter);
}
