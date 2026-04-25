namespace Kor.Inspections.App.Data.Models
{
    /// <summary>
    /// Canonical values for Booking.Status. Stored as strings in the DB
    /// (Booking.Status is `string`); these constants prevent typos.
    /// </summary>
    public static class BookingStatus
    {
        public const string Unassigned = "Unassigned";
        public const string Assigned = "Assigned";
        public const string Cancelled = "Cancelled";
        public const string Completed = "Completed";
    }

    /// <summary>
    /// Canonical values for BookingAction.ActionType.
    /// </summary>
    public static class BookingActionType
    {
        public const string Created = "Created";
        public const string Assigned = "Assigned";
        // Used by admin unassign audit rows as BookingActionType.Unassigned.
        public const string Unassigned = "Unassigned";
        public const string Edited = "Edited";
        public const string Cancelled = "Cancelled";
    }
}
