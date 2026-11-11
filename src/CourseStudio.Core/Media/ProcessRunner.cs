using System.Diagnostics;

namespace CourseStudio.Core.Media;

public sealed record ProcessOutput(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    public static async Task<ProcessOutput> RunAsync(string executable, string arguments,
        CancellationToken cancellationToken = default, Action<string?>? onOutput = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in SplitArguments(arguments)) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await using (cancellationToken.Register(() => { try { process.Kill(true); } catch { /* already exited */ } }))
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        return new ProcessOutput(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Splits an argument string honoring quotes, used only for logging/hand-built argument lists.</summary>
    public static List<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in arguments)
        {
            switch (ch)
            {
                case '"' when !inQuotes:
                    inQuotes = true;
                    break;
                case '"' when inQuotes:
                    inQuotes = false;
                    break;
                case ' ' or '\t' when !inQuotes:
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
