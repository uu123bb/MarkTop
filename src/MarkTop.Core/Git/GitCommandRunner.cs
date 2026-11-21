using System.Diagnostics;
using System.Text;

namespace MarkTop.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr;
}

/// <summary>
/// Runs the system git executable. We deliberately avoid libgit2 so the single-file app has no extra
/// native dependency, and so credentials reuse the user's existing Git for Windows setup.
/// </summary>
public sealed class GitCommandRunner
{
    private static readonly string[] GlobalArguments =
    {
        "-c", "core.quotepath=false",
        "-c", "i18n.logOutputEncoding=UTF-8",
        "-c", "core.autocrlf=false",
        "-c", "advice.detachedHead=false",
        "-c", "color.ui=false",
    };

    public GitCommandRunner(string? gitPath = null)
    {
        var resolved = string.IsNullOrWhiteSpace(gitPath) ? ResolveGitPath() : gitPath;
        IsAvailable = resolved is not null && File.Exists(resolved);
        GitPath = resolved ?? "git";
    }

    public string GitPath { get; }

    public bool IsAvailable { get; }

    public static string? ResolveGitPath()
    {
        var executable = "git.exe";
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // malformed PATH entry, ignore
            }
        }

        string[] known =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        };

        return known.FirstOrDefault(File.Exists);
    }

    public Task<GitResult> RunAsync(
        string? workingDirectory,
        IEnumerable<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default) =>
        RunAsync(workingDirectory, arguments, null, timeoutSeconds, cancellationToken);

    public Task<GitResult> RunAsync(
        string? workingDirectory,
        IEnumerable<string> arguments,
        string? standardInput = null,
        int timeoutSeconds = 90,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GitPath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in GlobalArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        return RunProcessAsync(startInfo, standardInput, timeoutSeconds, cancellationToken);
    }

    private static async Task<GitResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string? standardInput,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new GitResult(-1, string.Empty, "无法启动 git 进程。");
            }
        }
        catch (Exception ex)
        {
            return new GitResult(-1, string.Empty, $"无法启动 git：{ex.Message}");
        }

        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), CancellationToken.None);
                await process.StandardInput.FlushAsync(CancellationToken.None);
            }

            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                var timedOut = timeout.IsCancellationRequested;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // already gone
                }

                var partialOut = await SafeReadAsync(stdoutTask);
                var partialErr = await SafeReadAsync(stderrTask);
                return new GitResult(-1, partialOut, timedOut ? $"git 执行超时（{timeoutSeconds}s）。" : "已取消。", timedOut);
            }

            var stdout = await SafeReadAsync(stdoutTask);
            var stderr = await SafeReadAsync(stderrTask);
            return new GitResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }
}
