using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.App.Services
{
    internal static class DbUpdateExceptionClassifier
    {
        public static bool IsNamedUniqueConstraintViolation(
            DbUpdateException exception,
            params string[] constraintMarkers)
        {
            var baseException = exception.GetBaseException();

            if (baseException is SqlException sqlException &&
                sqlException.Number is 2601 or 2627)
            {
                if (constraintMarkers.Length == 0)
                    return true;

                return constraintMarkers.Any(marker =>
                    sqlException.Message.Contains(marker, StringComparison.Ordinal));
            }

            if (constraintMarkers.Length == 0)
                return false;

            var message = baseException.Message;
            return constraintMarkers.Any(marker =>
                message.Contains(marker, StringComparison.Ordinal));
        }
    }
}
