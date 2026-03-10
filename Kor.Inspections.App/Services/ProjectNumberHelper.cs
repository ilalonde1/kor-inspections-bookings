using System.Text.RegularExpressions;

namespace Kor.Inspections.App.Services
{
    public static class ProjectNumberHelper
    {
        private static readonly Regex Base5Regex =
            new(@"^\s*(\d{5})", RegexOptions.Compiled);

        // Returns the first 5 digits at the start (e.g. "30844-01" => "30844")
        // Returns "" if it doesn't start with 5 digits.
        public static string Base5(string? projectNumber)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                return "";

            var m = Base5Regex.Match(projectNumber);
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
