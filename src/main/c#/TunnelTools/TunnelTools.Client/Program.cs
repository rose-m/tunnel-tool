using System.Diagnostics;

namespace TunnelTools.Client;

public class Program
{
    private static readonly List<(Process Process, string Command)> _processes = new();
    private static readonly CancellationTokenSource _cts = new();

    public static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: program <ngrok_tcp_host> <port> <commands_file>");
            return;
        }

        if (!int.TryParse(args[0], out int tcpHost))
        {
            Console.WriteLine("Error: ngrok TCP host must be an integer");
            return;
        }

        if (!int.TryParse(args[1], out int port))
        {
            Console.WriteLine("Error: port must be an integer");
            return;
        }

        string commandsFile = args[2];
        if (!File.Exists(commandsFile))
        {
            Console.WriteLine($"Error: commands file '{commandsFile}' does not exist");
            return;
        }

        Console.WriteLine($"Starting processes with ngrok host {tcpHost} and port {port}");
        Console.WriteLine($"Reading commands from: {commandsFile}");
        
        // Set up CTRL-C handling
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately
            _cts.Cancel();
        };

        try
        {
            // Start the processes
            StartProcesses(tcpHost, port, commandsFile);

            // Monitor and restart processes until cancellation
            while (!_cts.Token.IsCancellationRequested)
            {
                var deadProcesses = _processes.Where(p => p.Process.HasExited).ToList();
                foreach (var (process, command) in deadProcesses)
                {
                    Console.WriteLine($"Process exited: {command}. Restarting...");
                    _processes.Remove((process, command));
                    StartSingleProcess(command);
                }
                Thread.Sleep(1000); // Check every second
            }
        }
        finally
        {
            CleanupProcesses();
        }
    }

    private static void StartProcesses(int ngrokHost, int ngrokPort, string commandsFile)
    {
        var commands = File.ReadAllLines(commandsFile)
            .Where(line => !string.IsNullOrWhiteSpace(line)) // Skip empty lines
            .Select(line => line.Replace("${ngrokHost}", ngrokHost.ToString())
                              .Replace("${ngrokPort}", ngrokPort.ToString()));

        foreach (var command in commands)
        {
            StartSingleProcess(command);
        }
    }

    private static void StartSingleProcess(string command)
    {
        var parts = command.Split(' ', 2); // Split into filename and arguments
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = parts[0],
                Arguments = parts[1],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        // Set up output handling before starting the process
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine($"[{command}] {e.Data}");
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                Console.Error.WriteLine($"[{command}] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add((process, command));
        Console.WriteLine($"Started process: {command}");
    }

    private static void CleanupProcesses()
    {
        Console.WriteLine("\nStopping all processes...");
        foreach (var (process, _) in _processes)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping process: {ex.Message}");
                }
            }
        }
        _processes.Clear();
        Console.WriteLine("All processes stopped.");
    }
}