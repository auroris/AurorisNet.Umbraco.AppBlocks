using System.Text.Json;
using AppBlocks.Forms;

namespace AppBlocks.Tests;

public sealed class FieldRuleTests
{
    private static readonly FieldCondition Cancelled = new("status", "text", "Is", "Cancelled", "");
    private static readonly FieldCondition Locked = new("locked", "checkbox", "Is", "true", "");
    private static readonly HashSet<string> Optional = [];

    [Fact]
    public void Hidden_and_locked_fields_reject_edits_and_preserve_stored_values()
    {
        var rules = new FieldRule[] {
            new("reason", "Show", "All", "", [Cancelled]),
            new("reason", "Require", "All", "", [Cancelled]),
            new("reason", "Make read only", "Any", "", [Locked]),
        };
        var stored = new FormRecord("1", null, new Dictionary<string, object?> { ["reason"] = "Prior reason" }, new());
        var sent = JsonSerializer.SerializeToElement(new { reason = "Changed" });
        var omitted = JsonSerializer.SerializeToElement(new { });
        var hidden = JsonSerializer.SerializeToElement(new { status = "Open", reason = "Changed" });
        Assert.Contains("reason", FieldRule.Validate(rules, hidden, sent, stored, Optional));
        Assert.Empty(FieldRule.Validate(rules, hidden, omitted, stored, Optional));
        var locked = JsonSerializer.SerializeToElement(new { status = "Cancelled", locked = true, reason = "Changed" });
        Assert.Contains("reason", FieldRule.Validate(rules, locked, sent, stored, Optional));
        var missing = JsonSerializer.SerializeToElement(new { status = "Cancelled" });
        Assert.Contains("reason", FieldRule.Validate(rules, missing, omitted, null, Optional));
    }

    [Theory]
    [InlineData("number", "9007199254740993", "9007199254740992", true)]
    [InlineData("number", "2", "10", false)]
    [InlineData("date", "2026-05-01", "2026-04-30", true)]
    [InlineData("datetime", "2026-05-01T12:00:00+02:00", "2026-05-01T10:01:00Z", false)]
    [InlineData("time", "09:00", "09:00:01", false)]
    public void Cross_field_validations_compare_typed_values(string kind, string end, string start, bool succeeds)
    {
        var values = JsonSerializer.SerializeToElement(new { start, end });
        var after = new FieldCondition("end", kind, "Is at least", "", "start");
        var empty = new FieldCondition("start", kind, "Is empty", "", "");
        var rule = new FieldRule("end", "Validate", "Any", "Check the end.", [empty, after]);
        Assert.Equal(succeeds, !FieldRule.Validate([rule], values, values, null, Optional).ContainsKey("end"));
    }
}
