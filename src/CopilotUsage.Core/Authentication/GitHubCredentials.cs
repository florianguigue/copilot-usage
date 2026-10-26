using System.Diagnostics;

namespace CopilotUsage.Core.Authentication;

// Credentials remain in memory or in gh's own credential storage. No token CLI argument or file.
public sealed class GitHubCredentials(string token, string method)
{
    public string Method { get; } = method;
    internal string Token { get; } = token;
    public override string ToString() => Method;

    public static async Task<GitHubCredentials?> ResolveAsync(CancellationToken cancellationToken)
    {
        var supplied = Environment.GetEnvironmentVariable("COPILOT_USAGE_TOKEN");
        if (!string.IsNullOrWhiteSpace(supplied)) return new(supplied.Trim(), "environment:COPILOT_USAGE_TOKEN");
        try
        {
            var result = await RunGhAsync(["auth", "token", "--hostname", "github.com"], cancellationToken);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)
                ? new(result.Output.Trim(), "gh:github.com") : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static async Task<bool> LoginAsync(CancellationToken cancellationToken)
    {
        // gh uses its registered OAuth client and Device Flow, subject to organization policies.
        var result = await RunGhAsync(["auth", "login", "--hostname", "github.com", "--web", "--git-protocol", "https"],
            cancellationToken, interactive: true);
        return result.ExitCode == 0;
    }

    private static async Task<(int ExitCode, string Output)> RunGhAsync(string[] args, CancellationToken ct, bool interactive = false)
    {
        var start = new ProcessStartInfo("gh")
        {
            UseShellExecute = false, CreateNoWindow = !interactive,
            RedirectStandardOutput = !interactive, RedirectStandardError = !interactive
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // Ignore GH_HOST and enterprise credentials. gh is explicitly scoped to github.com.
        start.Environment.Remove("GH_HOST");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("gh unavailable");
        var output = interactive ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync(ct);
        var error = interactive ? Task.FromResult("") : process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await error; // Drain without ever logging stderr: it may contain confidential information.
        return (process.ExitCode, await output);
    }
}
