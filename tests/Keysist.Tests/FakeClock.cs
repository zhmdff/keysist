using Keysist.Correlation;

public class FakeClock : IClock
{
    public long Now { get; set; } = 0;
    public long NowMs() => Now;
}
