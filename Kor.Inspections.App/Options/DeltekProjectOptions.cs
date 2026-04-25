using System.ComponentModel.DataAnnotations;

namespace Kor.Inspections.App.Options
{
    public class DeltekProjectOptions
    {
        [Required]
        public string OdbcDsn { get; set; } = string.Empty;

        [Required]
        public string Sql_ProjectByNumber { get; set; } = string.Empty;

        [Required]
        public string Sql_ProjectSearchByPrefix { get; set; } = string.Empty;
    }
}
