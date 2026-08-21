namespace RepositoryChecks;

internal static class RepositoryChecksProgram
{
    public static int Main(string[] arguments) =>
        RepositoryCheckCommand.Run(arguments, Console.Out, Console.Error);
}
