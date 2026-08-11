using Keysist.Config;

namespace Keysist.Actions;

public class ActionDispatcher
{
    private readonly IProcessLauncher _launcher;
    private readonly IKeySender _sender;

    public ActionDispatcher(IProcessLauncher launcher, IKeySender sender)
    {
        _launcher = launcher;
        _sender = sender;
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
            default:
                throw new ArgumentException($"Unknown action type '{action.Type}'");
        }
    }
}
