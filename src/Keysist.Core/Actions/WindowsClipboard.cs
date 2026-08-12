namespace Keysist.Actions;

public class WindowsClipboard : IClipboard
{
    public void SetText(string text) => System.Windows.Forms.Clipboard.SetText(text);
}
