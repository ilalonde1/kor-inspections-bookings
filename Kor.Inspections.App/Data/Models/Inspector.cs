namespace Kor.Inspections.App.Data.Models
{
    public class Inspector
    {
        public int InspectorId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int DailyMax { get; set; } = 8;
        public bool Enabled { get; set; } = true;
    }
}
