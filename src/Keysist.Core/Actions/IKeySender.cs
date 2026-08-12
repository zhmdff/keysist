using Keysist.Config;

namespace Keysist.Actions;

public interface IKeySender
{
    void Send(IReadOnlyList<SendStep> steps);

    /// <summary>Re-inject a single virtual-key tap (down+up), used when a tentative swallow was wrong.</summary>
    void SendVkeyTap(ushort virtualKey);
}
