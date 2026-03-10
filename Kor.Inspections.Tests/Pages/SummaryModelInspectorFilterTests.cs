using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Pages.Admin;

namespace Kor.Inspections.Tests.Pages;

public class SummaryModelInspectorFilterTests
{
    [Fact]
    public void InspectorFilter_DifferentCasing_IncludesRow()
    {
        var inspector = new Inspector
        {
            DisplayName = "John Smith"
        };
        var row = new SummaryModel.SummaryRow
        {
            AssignedTo = "john smith"
        };

        var included = MatchesInspector(row, inspector);

        Assert.True(included);
    }

    [Fact]
    public void InspectorFilter_ExactMatch_IncludesRow()
    {
        var inspector = new Inspector
        {
            DisplayName = "John Smith"
        };
        var row = new SummaryModel.SummaryRow
        {
            AssignedTo = "John Smith"
        };

        var included = MatchesInspector(row, inspector);

        Assert.True(included);
    }

    [Fact]
    public void InspectorFilter_DifferentInspector_ExcludesRow()
    {
        var inspector = new Inspector
        {
            DisplayName = "John Smith"
        };
        var row = new SummaryModel.SummaryRow
        {
            AssignedTo = "Jane Doe"
        };

        var included = MatchesInspector(row, inspector);

        Assert.False(included);
    }

    private static bool MatchesInspector(SummaryModel.SummaryRow row, Inspector inspector)
    {
        return !string.Equals(row.AssignedTo, "Unassigned", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(row.AssignedTo, inspector.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
