namespace Kor.Inspections.App.Services
{
    public static class BookingDisplayHelper
    {
        public static string FormatJobLine(
            string? projectNumberDisplay,
            string? projectName,
            string fallbackProjectNumber)
        {
            var displayNumber = string.IsNullOrWhiteSpace(projectNumberDisplay)
                ? (fallbackProjectNumber ?? string.Empty)
                : projectNumberDisplay;
            return string.IsNullOrWhiteSpace(projectName)
                ? displayNumber
                : $"{displayNumber} {projectName}";
        }

        public static string GetTimeDisplay(
            string? timePreference,
            DateTime startLocal,
            DateTime endLocal)
        {
            if (!string.IsNullOrWhiteSpace(timePreference))
            {
                return timePreference.ToUpper() switch
                {
                    "AM" => "Anytime AM",
                    "PM" => "Anytime PM",
                    _ => $"{startLocal:HH:mm} - {endLocal:HH:mm}"
                };
            }


            return $"{startLocal:HH:mm} - {endLocal:HH:mm}";
        }
    }
}
