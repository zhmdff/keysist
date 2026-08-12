namespace Keysist.Actions;

public interface IProcessLauncher
{
    void Launch(string path, string? args, string? workingDirectory);
}
