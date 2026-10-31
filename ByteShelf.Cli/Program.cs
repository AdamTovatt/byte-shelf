using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Mcp;

namespace ByteShelf.Cli
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            using CancellationTokenSource interrupted = new CancellationTokenSource();
            Console.CancelKeyPress += (_, press) =>
            {
                // The first Ctrl+C asks the command to stop cleanly; a second one ends the process.
                press.Cancel = !interrupted.IsCancellationRequested;
                interrupted.Cancel();
            };

            using ConfiguredClientSource clients = new ConfiguredClientSource(
                CliConfigStore.FromEnvironment(Environment.GetEnvironmentVariable),
                Environment.GetEnvironmentVariable);

            if (args.Length > 0 && args[0] == "--mcp")
            {
                await McpServerRunner.RunAsync(clients, args[1..], interrupted.Token);
                return CliApp.Succeeded;
            }

            return await new CliApp(clients, Terminal.FromConsole()).RunAsync(args, interrupted.Token);
        }
    }
}
