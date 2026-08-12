using Keysist.Actions;

public class FakeClipboard : IClipboard
{
    public List<string> Calls { get; } = new();
    public void SetText(string text) => Calls.Add(text);
}
