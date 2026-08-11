using Keysist.Actions;
using Keysist.Config;
using Xunit;

public class ActionDispatcherTests
{
    [Fact]
    public void Dispatch_Launch_CallsProcessLauncherWithPathArgsWorkingDir()
    {
        var launcher = new FakeProcessLauncher();
        var sender = new FakeKeySender();
        var dispatcher = new ActionDispatcher(launcher, sender);
        var action = new ActionConfig { Type = "launch", Path = "C:\\Tools\\foo.exe", Args = "--flag", WorkingDirectory = "C:\\Tools" };

        dispatcher.Dispatch(action);

        Assert.Single(launcher.Calls);
        Assert.Equal(("C:\\Tools\\foo.exe", "--flag", "C:\\Tools"), launcher.Calls[0]);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public void Dispatch_SendKeys_CallsKeySenderWithSteps()
    {
        var launcher = new FakeProcessLauncher();
        var sender = new FakeKeySender();
        var dispatcher = new ActionDispatcher(launcher, sender);
        var steps = new List<SendStep> { new() { Send = "vkey", Value = "VK_MEDIA_PLAY_PAUSE" } };
        var action = new ActionConfig { Type = "sendKeys", Steps = steps };

        dispatcher.Dispatch(action);

        Assert.Single(sender.Calls);
        Assert.Same(steps, sender.Calls[0]);
        Assert.Empty(launcher.Calls);
    }

    [Fact]
    public void Dispatch_UnknownType_ThrowsArgumentException()
    {
        var dispatcher = new ActionDispatcher(new FakeProcessLauncher(), new FakeKeySender());
        var action = new ActionConfig { Type = "bogus" };

        Assert.Throws<ArgumentException>(() => dispatcher.Dispatch(action));
    }
}
