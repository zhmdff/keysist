using Keysist.Config;

namespace Keysist.Actions;

public class ActionDispatcher
{
    private readonly IProcessLauncher _launcher;
    private readonly IKeySender _sender;
    private readonly IClipboard _clipboard;

    public ActionDispatcher(IProcessLauncher launcher, IKeySender sender, IClipboard clipboard)
    {
        _launcher = launcher;
        _sender = sender;
        _clipboard = clipboard;
    }

    public void Dispatch(ActionConfig action)
    {
        switch (action.Type)
        {
            case "launch":
                _launcher.Launch(action.Path ?? "", action.Args, action.WorkingDirectory);
                break;
            case "sendKeys":
                _sender.Send(action.Steps ?? new List<SendStep>());
                break;
            case "pasteText":
                DispatchPasteText(action);
                break;
            default:
                throw new ArgumentException($"Unknown action type '{action.Type}'");
        }
    }

    private void DispatchPasteText(ActionConfig action)
    {
        var text = action.Text ?? "";
        if (action.Method == "clipboard")
        {
            _clipboard.SetText(text);
            _sender.Send(new List<SendStep>
            {
                new() { Send = "keydown", Value = "LControlKey" },
                new() { Send = "vkey", Value = "V" },
                new() { Send = "keyup", Value = "LControlKey" },
            });
        }
        else
        {
            _sender.Send(new List<SendStep> { new() { Send = "text", Value = text } });
        }
    }
}
