using Keysist.Capture;
using Keysist.Config;

namespace Keysist.Services;

public static class ActionConfigBuilder
{
    public static List<SendStep> CaptureToSendSteps(CaptureResult result)
    {
        if (result.Keys.Count == 1)
            return [new SendStep { Send = "vkey", Value = result.Keys[0] }];

        if (result.Mode == CaptureMode.Sequence)
        {
            return result.Keys
                .Select(key => new SendStep { Send = "vkey", Value = key, DelayMs = 0 })
                .ToList();
        }

        var steps = new List<SendStep>();
        for (var i = 0; i < result.Keys.Count - 1; i++)
            steps.Add(new SendStep { Send = "keydown", Value = result.Keys[i] });

        steps.Add(new SendStep { Send = "vkey", Value = result.Keys[^1] });

        for (var i = result.Keys.Count - 2; i >= 0; i--)
            steps.Add(new SendStep { Send = "keyup", Value = result.Keys[i] });

        return steps;
    }

    public static bool TryBuildSendKeys(CaptureResult? result, out ActionConfig action)
    {
        action = new ActionConfig();
        if (result is not { Keys.Count: > 0 })
            return false;

        action.Type = "sendKeys";
        action.Steps = CaptureToSendSteps(result);
        return true;
    }

    public static bool TryBuildPasteText(string text, string method, out ActionConfig action)
    {
        action = new ActionConfig();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        action.Type = "pasteText";
        action.Text = text;
        action.Method = string.IsNullOrWhiteSpace(method) ? "type" : method;
        return true;
    }

    public static bool TryBuildLaunch(string path, string? args, string? workDir, out ActionConfig action)
    {
        action = new ActionConfig();
        if (string.IsNullOrWhiteSpace(path))
            return false;

        action.Type = "launch";
        action.Path = path.Trim();
        action.Args = string.IsNullOrWhiteSpace(args) ? null : args;
        action.WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir;
        return true;
    }

    public static string TriggerModeFromCapture(CaptureMode mode) => mode switch
    {
        CaptureMode.Chord => "chord",
        CaptureMode.Sequence => "sequence",
        _ => "key",
    };
}
