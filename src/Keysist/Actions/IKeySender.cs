using Keysist.Config;

namespace Keysist.Actions;

public interface IKeySender
{
    void Send(IReadOnlyList<SendStep> steps);
}
