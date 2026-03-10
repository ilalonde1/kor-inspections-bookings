using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    public class DeltekProjectOptions
    {
        public string OdbcDsn { get; set; } = string.Empty;
        public string Sql_ProjectByNumber { get; set; } = string.Empty;
        public string Sql_ProjectSearchByPrefix { get; set; } = string.Empty;
    }

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
        private readonly DeltekProjectOptions _options;
        private readonly ILogger<DeltekProjectService> _logger;

        public DeltekProjectService(
            IOptions<DeltekProjectOptions> options,
            ILogger<DeltekProjectService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        private string ConnectionString => _options.OdbcDsn;

        public async Task<DeltekProjectInfo?> GetProjectByNumberAsync(
            string projectNumber,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                return null;

            if (string.IsNullOrWhiteSpace(_options.Sql_ProjectByNumber))
            {
                _logger.LogWarning("Sql_ProjectByNumber not configured.");
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var conn = new OdbcConnection(ConnectionString);
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandTimeout = OdbcCommandTimeoutSeconds;
                    cmd.CommandText = _options.Sql_ProjectByNumber;
                    AddParametersForPlaceholders(cmd, projectNumber.Trim());

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
                        projectNumber);
                    return null;
                }
            }, ct);
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

            return await Task.Run(() =>
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
            }, ct);
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
