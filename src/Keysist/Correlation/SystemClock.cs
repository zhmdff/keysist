namespace Keysist.Correlation;

public class SystemClock : IClock
{
    public long NowMs() => Environment.TickCount64;
}
