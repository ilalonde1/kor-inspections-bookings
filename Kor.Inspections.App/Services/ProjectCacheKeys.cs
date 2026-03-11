namespace Kor.Inspections.App.Services;

internal static class ProjectCacheKeys
{
    internal static string BuildVerificationKey(string projectNumber, string domain)
        => $"proj-bootstrap:{projectNumber}|{domain.Trim().ToLowerInvariant()}";
}
