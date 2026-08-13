namespace MeetingTranscriber.Cli;

/// <summary>
/// The console this program is run from. Everything it does is <see cref="Cli.Run"/>'s, which is
/// what lets the whole command line be exercised without a process around it.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => Cli.Run(args, Console.Out, Console.Error);
}
