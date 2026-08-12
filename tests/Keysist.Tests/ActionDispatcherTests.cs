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
        var clipboard = new FakeClipboard();
        var dispatcher = new ActionDispatcher(launcher, sender, clipboard);
        var action = new ActionConfig { Type = "launch", Path = "C:\\Tools\\foo.exe", Args = "--flag", WorkingDirectory = "C:\\Tools" };

        dispatcher.Dispatch(action);

        Assert.Single(launcher.Calls);
        Assert.Equal(("C:\\Tools\\foo.exe", "--flag", "C:\\Tools"), launcher.Calls[0]);
        Assert.Empty(sender.Calls);
        Assert.Empty(clipboard.Calls);
    }

    [Fact]
    public void Dispatch_SendKeys_CallsKeySenderWithSteps()
    {
        var launcher = new FakeProcessLauncher();
        var sender = new FakeKeySender();
        var clipboard = new FakeClipboard();
        var dispatcher = new ActionDispatcher(launcher, sender, clipboard);
        var steps = new List<SendStep> { new() { Send = "vkey", Value = "VK_MEDIA_PLAY_PAUSE" } };
        var action = new ActionConfig { Type = "sendKeys", Steps = steps };

        dispatcher.Dispatch(action);

        Assert.Single(sender.Calls);
        Assert.Same(steps, sender.Calls[0]);
        Assert.Empty(launcher.Calls);
        Assert.Empty(clipboard.Calls);
    }

    [Fact]
    public void Dispatch_PasteText_Type_CallsKeySenderWithTextStep()
    {
        var launcher = new FakeProcessLauncher();
        var sender = new FakeKeySender();
        var clipboard = new FakeClipboard();
        var dispatcher = new ActionDispatcher(launcher, sender, clipboard);
        var action = new ActionConfig { Type = "pasteText", Text = "hello", Method = "type" };

        dispatcher.Dispatch(action);

        Assert.Empty(launcher.Calls);
        Assert.Empty(clipboard.Calls);
        Assert.Single(sender.Calls);
        Assert.Single(sender.Calls[0]);
        Assert.Equal("text", sender.Calls[0][0].Send);
        Assert.Equal("hello", sender.Calls[0][0].Value);
    }

    [Fact]
    public void Dispatch_PasteText_Clipboard_SetsClipboardAndSendsCtrlV()
    {
        var launcher = new FakeProcessLauncher();
        var sender = new FakeKeySender();
        var clipboard = new FakeClipboard();
        var dispatcher = new ActionDispatcher(launcher, sender, clipboard);
        var action = new ActionConfig { Type = "pasteText", Text = "hello", Method = "clipboard" };

        dispatcher.Dispatch(action);

        Assert.Empty(launcher.Calls);
        Assert.Single(clipboard.Calls);
        Assert.Equal("hello", clipboard.Calls[0]);
        Assert.Single(sender.Calls);
        var steps = sender.Calls[0];
        Assert.Equal(3, steps.Count);
        Assert.Equal("keydown", steps[0].Send);
        Assert.Equal("LControlKey", steps[0].Value);
        Assert.Equal("vkey", steps[1].Send);
        Assert.Equal("V", steps[1].Value);
        Assert.Equal("keyup", steps[2].Send);
        Assert.Equal("LControlKey", steps[2].Value);
    }

    [Fact]
    public void Dispatch_UnknownType_ThrowsArgumentException()
    {
        var dispatcher = new ActionDispatcher(new FakeProcessLauncher(), new FakeKeySender(), new FakeClipboard());
        var action = new ActionConfig { Type = "bogus" };

        Assert.Throws<ArgumentException>(() => dispatcher.Dispatch(action));
    }
}
