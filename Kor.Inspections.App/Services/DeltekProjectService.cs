using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Inspections.App.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    public sealed class DeltekProjectInfo
    {
        public string ProjectNumber { get; init; } = string.Empty;
        public string? ProjectName { get; init; }
        public string? Address { get; init; }
        public string? SiteContactName { get; init; }
        public string? SiteContactPhone { get; init; }
    }

    public class DeltekProjectService
    {
        private const int OdbcCommandTimeoutSeconds = 10;
        private const int MaxConcurrentCalls = 4;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
        private static readonly SemaphoreSlim OdbcConcurrencyGate = new(MaxConcurrentCalls, MaxConcurrentCalls);
        private readonly DeltekProjectOptions _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DeltekProjectService> _logger;

        public DeltekProjectService(
            IOptions<DeltekProjectOptions> options,
            IMemoryCache cache,
            ILogger<DeltekProjectService> logger)
        {
            _options = options.Value;
            _cache = cache;
            _logger = logger;
        }

        private string ConnectionString => _options.OdbcDsn;

        public async Task<DeltekProjectInfo?> GetProjectByNumberAsync(
            string projectNumber,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                return null;

            var normalizedProject = projectNumber.Trim();
            if (string.IsNullOrWhiteSpace(_options.Sql_ProjectByNumber))
            {
                _logger.LogWarning("Sql_ProjectByNumber not configured.");
                return null;
            }

            var cacheKey = BuildProjectCacheKey(normalizedProject);
            if (_cache.TryGetValue<DeltekProjectInfo?>(cacheKey, out var cachedProject))
                return cachedProject;

            var projectInfo = await ExecuteBoundedAsync(() => Task.Run(() =>
            {
                try
                {
                    using var conn = new OdbcConnection(ConnectionString);
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandTimeout = OdbcCommandTimeoutSeconds;
                    cmd.CommandText = _options.Sql_ProjectByNumber;
                    AddParametersForPlaceholders(cmd, normalizedProject);

                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read())
                        return null;

                    return new DeltekProjectInfo
                    {
                        ProjectNumber = GetStringSafe(reader, "ProjectNumber", "Proj", "WBS1"),
                        ProjectName = GetStringSafe(reader, "ProjectName", "Name", "ProjectDescription"),
                        Address = GetStringSafe(reader, "Address", "CLAddress"),
                        SiteContactName = GetStringSafe(reader, "SiteContactName"),
                        SiteContactPhone = GetStringSafe(reader, "SiteContactPhone")
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error reading project '{ProjectNumber}' from Deltek.",
                        normalizedProject);
                    return null;
                }
            }, ct), ct);

            _cache.Set(cacheKey, projectInfo, CacheTtl);
            return projectInfo;
        }

        public async Task<IReadOnlyList<DeltekProjectInfo>> SearchProjectsAsync(
            string term,
            int maxResults = 20,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Array.Empty<DeltekProjectInfo>();

            if (string.IsNullOrWhiteSpace(_options.Sql_ProjectSearchByPrefix))
            {
                _logger.LogWarning("Sql_ProjectSearchByPrefix not configured.");
                return Array.Empty<DeltekProjectInfo>();
            }

            var capped = Math.Clamp(maxResults, 1, 50);
            var trimmed = term.Trim();
            var cacheKey = BuildSearchCacheKey(trimmed, capped);

            if (_cache.TryGetValue<IReadOnlyList<DeltekProjectInfo>>(cacheKey, out var cachedResults) &&
                cachedResults is not null)
                return cachedResults;

            var results = await ExecuteBoundedAsync(() => Task.Run(() =>
            {
                try
                {
                    var prefixResults = QueryProjects(trimmed + "%", capped, ct);
                    if (prefixResults.Count >= capped)
                        return prefixResults
                            .OrderBy(p => GetSearchRank(trimmed, p))
                            .ThenBy(p => p.ProjectNumber, StringComparer.OrdinalIgnoreCase)
                            .Take(capped)
                            .ToList();

                    // Also search contains to support non-prefix keyword searches.
                    var containsResults = QueryProjects("%" + trimmed + "%", capped, ct);
                    if (containsResults.Count == 0)
                        return (IReadOnlyList<DeltekProjectInfo>)prefixResults;

                    if (prefixResults.Count == 0)
                        return (IReadOnlyList<DeltekProjectInfo>)containsResults;

                    var merged = new List<DeltekProjectInfo>(capped);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var item in prefixResults)
                    {
                        if (!seen.Add(item.ProjectNumber))
                            continue;

                        merged.Add(item);
                        if (merged.Count >= capped)
                            return merged;
                    }

                    foreach (var item in containsResults)
                    {
                        if (!seen.Add(item.ProjectNumber))
                            continue;

                        merged.Add(item);
                    }

                    return merged
                        .OrderBy(p => GetSearchRank(trimmed, p))
                        .ThenBy(p => p.ProjectNumber, StringComparer.OrdinalIgnoreCase)
                        .Take(capped)
                        .ToList();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching Deltek projects for term '{Term}'.", term);
                    return Array.Empty<DeltekProjectInfo>();
                }
            }, ct), ct);

            _cache.Set(cacheKey, results, CacheTtl);
            return results;
        }

        private static string BuildProjectCacheKey(string projectNumber)
        {
            return $"deltek:project:{projectNumber.ToUpperInvariant()}";
        }

        private static string BuildSearchCacheKey(string term, int maxResults)
        {
            return $"deltek:search:{maxResults}:{term.ToUpperInvariant()}";
        }

        private async Task<T> ExecuteBoundedAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            await OdbcConcurrencyGate.WaitAsync(ct);
            try
            {
                return await operation();
            }
            finally
            {
                OdbcConcurrencyGate.Release();
            }
        }

        private List<DeltekProjectInfo> QueryProjects(string like, int cap, CancellationToken ct)
        {
            using var conn = new OdbcConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = OdbcCommandTimeoutSeconds;
            cmd.CommandText = _options.Sql_ProjectSearchByPrefix;
            AddParametersForPlaceholders(cmd, like);

            using var reader = cmd.ExecuteReader();
            var results = new List<DeltekProjectInfo>(cap);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();

                var projectNumber = GetStringSafe(reader, "ProjectNumber", "Proj", "WBS1");
                if (string.IsNullOrWhiteSpace(projectNumber))
                    continue;

                if (!seen.Add(projectNumber))
                    continue;

                results.Add(new DeltekProjectInfo
                {
                    ProjectNumber = projectNumber,
                    ProjectName = GetStringSafe(reader, "ProjectName", "Name", "ProjectDescription"),
                    Address = GetStringSafe(reader, "Address", "CLAddress"),
                    SiteContactName = GetStringSafe(reader, "SiteContactName"),
                    SiteContactPhone = GetStringSafe(reader, "SiteContactPhone")
                });

                if (results.Count >= cap)
                    break;
            }

            return results;
        }

        private static void AddParametersForPlaceholders(OdbcCommand cmd, string value)
        {
            var count = cmd.CommandText.Count(c => c == '?');
            for (var i = 0; i < count; i++)
            {
                cmd.Parameters.AddWithValue($"@p{i + 1}", value);
            }
        }

        private static string GetStringSafe(OdbcDataReader reader, params string[] candidates)
        {
            foreach (var name in candidates)
            {
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (!string.Equals(reader.GetName(ordinal), name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!reader.IsDBNull(ordinal))
                        return reader.GetValue(ordinal)?.ToString()?.Trim() ?? string.Empty;

                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private static int GetSearchRank(string term, DeltekProjectInfo project)
        {
            var q = (term ?? string.Empty).Trim();
            if (q.Length == 0)
                return 99;

            var number = (project.ProjectNumber ?? string.Empty).Trim();
            var name = (project.ProjectName ?? string.Empty).Trim();

            if (number.Equals(q, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (number.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                return 1;
            if (number.Contains(q, StringComparison.OrdinalIgnoreCase))
                return 2;

            if (name.Equals(q, StringComparison.OrdinalIgnoreCase))
                return 3;
            if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                return 4;
            if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
                return 5;

            return 6;
        }
    }
}
