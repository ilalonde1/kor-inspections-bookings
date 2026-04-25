using System.ComponentModel.DataAnnotations;

namespace Kor.Inspections.App.Options
{
    public class NotificationOptions
    {
        [Required]
        public string FromMailbox { get; set; } = string.Empty;

        [Required]
        public string AdminRecipientEmail { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }
}
