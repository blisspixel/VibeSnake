namespace VibeSnake.JevPlay;

public static class JevPlayEntry
{
    public static Task<int> Main(string[] args) =>
        JevPlayCommand.RunAsync(args ?? [], Console.Out, Console.Error);
}
