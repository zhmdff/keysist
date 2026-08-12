using Keysist.Actions;
using Keysist.Config;

public class FakeKeySender : IKeySender
{
    public List<IReadOnlyList<SendStep>> Calls { get; } = new();
    public List<ushort> VkeyTaps { get; } = new();
    public void Send(IReadOnlyList<SendStep> steps) => Calls.Add(steps);
    public void SendVkeyTap(ushort virtualKey) => VkeyTaps.Add(virtualKey);
}
