using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Microsoft.EntityFrameworkCore;

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

        public static string? ResolveAssignedToDisplay(
            string? assignedTo,
            IReadOnlyDictionary<string, string> inspectorsByEmail,
            string? unassignedDisplay = BookingStatus.Unassigned)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return unassignedDisplay;

            return inspectorsByEmail.TryGetValue(assignedTo, out var displayName)
                ? displayName
                : assignedTo;
        }

        public static async Task<string?> ResolveAssignedToDisplayAsync(
            string? assignedTo,
            InspectionsContext db,
            string? unassignedDisplay = null)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return unassignedDisplay;

            var displayName = await db.Inspectors
                .AsNoTracking()
                .Where(i => i.Email == assignedTo)
                .Select(i => i.DisplayName)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(displayName) ? assignedTo : displayName;
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
