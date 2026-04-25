namespace Kor.Inspections.App.Services
{
    public static class EmailHelper
    {
        /// <summary>
        /// Returns the lowercase domain portion of an email address, or
        /// empty string if no '@' boundary is found.
        /// </summary>
        public static string GetDomain(string? email)
        {
            if (string.IsNullOrEmpty(email))
                return string.Empty;

            var at = email.LastIndexOf('@');
            return (at > 0 && at < email.Length - 1)
                ? email[(at + 1)..].Trim().ToLowerInvariant()
                : string.Empty;
        }
    }
}
