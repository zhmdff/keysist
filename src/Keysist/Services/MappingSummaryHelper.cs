using Keysist.Config;

namespace Keysist.Services;

public static class MappingSummaryHelper
{
    public static string FormatTriggerSummary(KeyMapping mapping)
    {
        var trigger = mapping.GetEffectiveTrigger();
        return trigger.Mode.ToLowerInvariant() switch
        {
            "chord" => string.Join("+", trigger.Keys),
            "sequence" => string.Join(" → ", trigger.Keys),
            _ => trigger.Keys.Count > 0 ? trigger.Keys[0] : mapping.Key,
        };
    }

    public static string FormatActionSummary(ActionConfig action)
    {
        return action.Type.ToLowerInvariant() switch
        {
            "launch" => $"Launch: {action.Path}",
            "sendkeys" => FormatSendKeysSummary(action),
            "pastetext" => FormatPasteTextSummary(action),
            _ => action.Type,
        };
    }

    private static string FormatSendKeysSummary(ActionConfig action)
    {
        if (action.Steps is not { Count: > 0 })
            return "Send keys";

        if (action.Steps.Count == 1)
            return $"Send keys: {action.Steps[0].Value}";

        var parts = action.Steps
            .Where(s => s.Send is "vkey" or "text")
            .Select(s => s.Value)
            .Take(4)
            .ToList();
        var summary = string.Join(", ", parts);
        if (action.Steps.Count > parts.Count)
            summary += ", …";
        return $"Send keys: {summary}";
    }

    private static string FormatPasteTextSummary(ActionConfig action)
    {
        var method = string.IsNullOrWhiteSpace(action.Method) ? "type" : action.Method;
        var text = action.Text ?? "";
        if (text.Length > 40)
            text = text[..37] + "…";
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return $"Paste ({method}): {text}";
    }
}
