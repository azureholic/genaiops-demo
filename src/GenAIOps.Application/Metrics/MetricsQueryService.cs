using System.Globalization;
using System.Text;
using System.Text.Json;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;

namespace GenAIOps.Application.Metrics;

public sealed class MetricsQueryService(IRepository<MetricSnapshotRecord> repository)
    : IMetricsQueryService
{
    public async Task<MetricsPage> QueryAsync(
        MetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        Validate(query);
        List<MetricSnapshotRecord> snapshots = [];
        string? repositoryToken = null;
        do
        {
            RepositoryPage<MetricSnapshotRecord> page = await repository.QueryAsync(
                new RecordQuery(
                    query.RegistryId,
                    "metricSnapshot",
                    RecordQuery.MaximumPageSize,
                    repositoryToken),
                cancellationToken);
            snapshots.AddRange(page.Items.Select(item => item.Value));
            repositoryToken = page.ContinuationToken;
        }
        while (repositoryToken is not null);

        MetricSnapshotRecord[] filtered = snapshots
            .Where(snapshot =>
                (query.PromptVersion is null
                    || string.Equals(
                        snapshot.PromptVersion,
                        query.PromptVersion,
                        StringComparison.Ordinal))
                && (!query.From.HasValue || snapshot.WindowEnd > query.From.Value)
                && (!query.To.HasValue || snapshot.WindowStart < query.To.Value))
            .GroupBy(
                snapshot => new
                {
                    snapshot.PartitionKey,
                    snapshot.PromptVersion,
                    snapshot.WindowStart,
                    snapshot.WindowEnd,
                })
            .Select(
                revisions => revisions
                    .OrderByDescending(snapshot => snapshot.SampleCount)
                    .ThenByDescending(snapshot => snapshot.GeneratedAt)
                    .ThenByDescending(snapshot => snapshot.Id, StringComparer.Ordinal)
                    .First())
            .OrderByDescending(snapshot => snapshot.WindowStart)
            .ThenBy(snapshot => snapshot.PromptVersion, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Id, StringComparer.Ordinal)
            .ToArray();
        MetricsCursor? cursor = DecodeCursor(query.ContinuationToken);
        if (cursor is not null)
        {
            EnsureCursorMatches(query, cursor);
            filtered = filtered.Where(snapshot => IsAfter(snapshot, cursor)).ToArray();
        }

        MetricSnapshotRecord[] items = filtered.Take(query.PageSize).ToArray();
        string? next = items.Length == query.PageSize && filtered.Length > items.Length
            ? EncodeCursor(query, items[^1])
            : null;
        return new MetricsPage(items, next);
    }

    private static void Validate(MetricsQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.RegistryId);
        if (query.PromptVersion is { } version && string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Prompt version cannot be empty.",
                nameof(query.PromptVersion));
        }

        if (query.PageSize is <= 0 or > RecordQuery.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query.PageSize),
                $"Page size must be between 1 and {RecordQuery.MaximumPageSize}.");
        }

        if (query.From is { } from && from.Offset != TimeSpan.Zero
            || query.To is { } to && to.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time filters must use UTC offsets.");
        }

        if (query.From.HasValue && query.To.HasValue && query.To <= query.From)
        {
            throw new ArgumentException("The 'to' filter must be after 'from'.");
        }
    }

    private static bool IsAfter(MetricSnapshotRecord snapshot, MetricsCursor cursor) =>
        snapshot.WindowStart < cursor.WindowStart
        || snapshot.WindowStart == cursor.WindowStart
            && (string.CompareOrdinal(snapshot.PromptVersion, cursor.PromptVersion) > 0
                || string.Equals(
                    snapshot.PromptVersion,
                    cursor.PromptVersion,
                    StringComparison.Ordinal)
                    && string.CompareOrdinal(snapshot.Id, cursor.Id) > 0);

    private static string EncodeCursor(MetricsQuery query, MetricSnapshotRecord last)
    {
        MetricsCursor cursor = new(
            query.RegistryId,
            query.PromptVersion,
            query.From,
            query.To,
            last.WindowStart,
            last.PromptVersion,
            last.Id);
        string base64 = Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(cursor));
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static MetricsCursor? DecodeCursor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<MetricsCursor>(
                Convert.FromBase64String(base64))
                ?? throw new FormatException();
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            throw new ArgumentException(
                "The metrics continuation token is invalid.",
                nameof(token),
                exception);
        }
    }

    private static void EnsureCursorMatches(MetricsQuery query, MetricsCursor cursor)
    {
        if (!string.Equals(query.RegistryId, cursor.RegistryId, StringComparison.Ordinal)
            || !string.Equals(query.PromptVersion, cursor.FilterVersion, StringComparison.Ordinal)
            || query.From != cursor.From
            || query.To != cursor.To)
        {
            throw new ArgumentException(
                "The metrics continuation token does not match the active filters.",
                nameof(query.ContinuationToken));
        }
    }

    private sealed record MetricsCursor(
        string RegistryId,
        string? FilterVersion,
        DateTimeOffset? From,
        DateTimeOffset? To,
        DateTimeOffset WindowStart,
        string PromptVersion,
        string Id);
}
