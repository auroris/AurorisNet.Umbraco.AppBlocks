using System.Globalization;
using System.Text.Json;
using AppBlocks.Blocks;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace AppBlocks.Forms;

public sealed record FieldCondition(string Field, string Kind, string Comparison, string Literal, string OtherField)
{
    internal static string Text(JsonElement values, string field)
        => values.TryGetProperty(field, out var value) ? value.ValueKind switch {
            JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "", _ => value.ToString(),
        } : "";

    internal bool Matches(JsonElement values)
    {
        var left = Text(values, Field);
        var right = OtherField.Length == 0 ? Literal : Text(values, OtherField);
        if (Comparison == "Is empty") return string.IsNullOrWhiteSpace(left);
        if (Comparison == "Has a value") return !string.IsNullOrWhiteSpace(left);
        int order;
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            if (Comparison is not ("Is" or "Is not")) return false;
            order = string.CompareOrdinal(left.Trim(), right.Trim());
        }
        else if (Kind == "number")
        {
            if (!decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                || !decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return false;
            order = a.CompareTo(b);
        }
        else if (Kind == "datetime")
        {
            if (!DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a)
                || !DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var b)) return false;
            order = a.CompareTo(b);
        }
        else if (Kind == "time")
        {
            if (!TimeOnly.TryParse(left, CultureInfo.InvariantCulture, out var a) || !TimeOnly.TryParse(right, CultureInfo.InvariantCulture, out var b)) return false;
            order = a.CompareTo(b);
        }
        else order = string.CompareOrdinal(left, right);
        return Comparison switch {
            "Is" => order == 0, "Is not" => order != 0, "Is greater than" => order > 0, "Is at least" => order >= 0,
            "Is less than" => order < 0, "Is at most" => order <= 0, _ => false,
        };
    }
}

public sealed record FieldRule(string Field, string Action, string Match, string Message, IReadOnlyList<FieldCondition> Conditions)
{
    internal bool Matches(JsonElement values) => Match == "Any" ? Conditions.Any(c => c.Matches(values)) : Conditions.All(c => c.Matches(values));

    private static FieldRule Read(Func<string, string> read, IEnumerable<Func<string, string>> conditions, IReadOnlyList<FormField> contract) =>
        new(read("field"), read("action"), read("match"), read("message"), conditions.Select(c => new FieldCondition(c("field"),
            contract.FirstOrDefault(f => f.Key == c("field"))?.Kind ?? "", c("comparison"), c("literal"), c("otherField"))).ToArray());

    internal static IReadOnlyList<FieldRule> Published(IPublishedElement form, IReadOnlyList<FormField> contract)
    {
        static Func<string, string> Properties(IPublishedElement element) => name => element.Value<string>(name) ?? "";
        return (form.Value<BlockListModel>("fieldRules") ?? BlockListModel.Empty).Select(rule => Read(Properties(rule.Content),
            (rule.Content.Value<BlockListModel>("conditions") ?? BlockListModel.Empty).Select(c => Properties(c.Content)), contract)).ToArray();
    }

    internal static IReadOnlyList<FieldRule> Authored(string raw, IReadOnlyList<FormField> contract)
    {
        static IEnumerable<BlockItemData> Items(string json) {
            if (string.IsNullOrWhiteSpace(json)) return [];
            var list = BlockValueSets.Parse(json);
            var items = list.ContentData.ToDictionary(b => b.Key);
            return BlockValueSets.Layout(list).Select(i => items[i.ContentKey]);
        }
        static Func<string, string> Properties(BlockItemData block) => alias => BlockValueSets.Text(block, alias);
        return Items(raw).Select(rule => Read(Properties(rule), Items(BlockValueSets.Text(rule, "conditions")).Select(Properties), contract)).ToArray();
    }

    internal static IEnumerable<string> Problems(IReadOnlyList<FieldRule> rules, IEnumerable<string> bindings)
    {
        var fields = bindings.ToHashSet(StringComparer.Ordinal);
        var dependencies = rules.Where(r => r.Action is "Show" or "Hide" or "Make read only").GroupBy(r => r.Field)
            .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Conditions).SelectMany(c => new[] { c.Field, c.OtherField })
                .Where(field => field.Length > 0).Distinct().ToArray());
        var checking = new HashSet<string>();
        var checkedFields = new HashSet<string>();
        bool HasCycle(string field) {
            if (checkedFields.Contains(field) || !dependencies.TryGetValue(field, out var next)) return false;
            if (!checking.Add(field)) return true;
            if (next.Any(HasCycle)) return true;
            checking.Remove(field);
            checkedFields.Add(field);
            return false;
        }
        if (dependencies.Keys.Any(HasCycle)) yield return "A visibility or read-only rule cannot depend on itself, directly or through another field.";
        foreach (var rule in rules)
        {
            if (!fields.Contains(rule.Field)) yield return $"Add the rule's target field '{rule.Field}' to the form.";
            if (rule.Action is not ("Show" or "Hide" or "Require" or "Make read only" or "Validate")) yield return "Choose a field rule action.";
            if (rule.Match is not ("" or "All" or "Any") || rule.Conditions.Count == 0) yield return "Add at least one condition and choose All or Any.";
            if (rule.Action == "Validate" && string.IsNullOrWhiteSpace(rule.Message)) yield return "Enter a validation message for " + rule.Field + ".";
            foreach (var c in rule.Conditions)
            {
                if (!fields.Contains(c.Field) || c.OtherField.Length > 0 && !fields.Contains(c.OtherField)) yield return "Choose comparison fields that appear on this form.";
                if (c.Comparison is not ("Is" or "Is not" or "Is empty" or "Has a value" or "Is greater than" or "Is at least" or "Is less than" or "Is at most"))
                    yield return "Choose a field comparison.";
                if (c.Literal.Length > 0 && c.OtherField.Length > 0) yield return "Choose either a comparison field or a fixed value.";
            }
        }
    }

    internal static Dictionary<string, string[]> Validate(IReadOnlyList<FieldRule> rules, JsonElement values, JsonElement submitted, FormRecord? stored, IReadOnlySet<string> requiredOnForm)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var group in rules.GroupBy(r => r.Field))
        {
            var field = group.Key;
            bool Active(string action) => group.Any(r => r.Action == action && r.Matches(values));
            var hidden = group.Any(r => r.Action == "Show" && !r.Matches(values)) || Active("Hide");
            if (hidden || Active("Make read only"))
            {
                if (submitted.TryGetProperty(field, out var sent) && (stored is null || !stored.Values.TryGetValue(field, out var old)
                    || !DataSources.FieldValues.Unchanged(sent, old))) errors[field] = ["This field cannot be changed through this form."];
                continue;
            }
            if (string.IsNullOrWhiteSpace(FieldCondition.Text(values, field))) {
                if (requiredOnForm.Contains(field) || Active("Require")) errors[field] = ["A value is required."];
                continue;
            }
            var messages = group.Where(r => r.Action == "Validate" && !r.Matches(values)).Select(r => r.Message).Distinct().ToArray();
            if (messages.Length > 0) errors[field] = messages;
        }
        return errors;
    }
}
