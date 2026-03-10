namespace Kor.Inspections.App.Options
{
    public class AppOptions
    {
        /// <summary>
        /// Base URL for the public site, e.g. https://bookings.korstructural.com
        /// or https://localhost:7074 in dev. No trailing slash required.
        /// </summary>
        public string? PublicBaseUrl { get; set; }
    }
}
