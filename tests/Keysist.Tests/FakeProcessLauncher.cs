using Keysist.Actions;

public class FakeProcessLauncher : IProcessLauncher
{
    public List<(string Path, string? Args, string? WorkingDirectory)> Calls { get; } = new();
    public void Launch(string path, string? args, string? workingDirectory) => Calls.Add((path, args, workingDirectory));
}
