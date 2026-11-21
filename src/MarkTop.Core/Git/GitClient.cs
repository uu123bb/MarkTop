namespace MarkTop.Core.Git;

public sealed class GitClient
{
    private static readonly string[] ReadmeNames = { "README.md", "README.markdown", "README.mdown", "readme.md" };

    private readonly GitCommandRunner _runner;
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public GitClient(GitCommandRunner? runner = null)
    {
        _runner = runner ?? new GitCommandRunner();
    }

    public GitCommandRunner Runner => _runner;

    public bool IsAvailable => _runner.IsAvailable;

    public async Task<(bool Ok, string Version, string? Error)> VerifyAsync(CancellationToken ct = default)
    {
        if (!_runner.IsAvailable)
        {
            return (false, string.Empty,
                "未找到 git。请安装 Git for Windows，或把 git.exe 加入 PATH 后重试。");
        }

        var result = await _runner.RunAsync(null, new[] { "--version" }, 20, ct);
        return result.Success
            ? (true, result.StdOut.Trim(), null)
            : (false, string.Empty, GitErrors.Classify(result).Message);
    }

    public async Task<(string? Name, string? Email)> GetIdentityAsync(CancellationToken ct = default)
    {
        var name = await _runner.RunAsync(null, new[] { "config", "--get", "user.name" }, 20, ct);
        var email = await _runner.RunAsync(null, new[] { "config", "--get", "user.email" }, 20, ct);
        return (
            name.Success ? name.StdOut.Trim() : null,
            email.Success ? email.StdOut.Trim() : null);
    }

    public async Task SetLocalIdentityAsync(string repositoryPath, string name, string email, CancellationToken ct = default)
    {
        await _runner.RunAsync(repositoryPath, new[] { "config", "--local", "user.name", name }, 20, ct);
        await _runner.RunAsync(repositoryPath, new[] { "config", "--local", "user.email", email }, 20, ct);
    }

    public Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult(Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")));
    }

    public async Task<string?> GetRemoteUrlAsync(string repositoryPath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(repositoryPath, new[] { "remote", "get-url", "origin" }, 20, ct);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<string> GetCurrentBranchAsync(string repositoryPath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(repositoryPath, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, 20, ct);
        var branch = result.Success ? result.StdOut.Trim() : string.Empty;
        if (branch is "HEAD" or "" || branch.StartsWith("fatal", StringComparison.OrdinalIgnoreCase))
        {
            // Unborn branch: ask git for the configured initial branch name instead of assuming master/main.
            var symbolic = await _runner.RunAsync(repositoryPath,
                new[] { "symbolic-ref", "--short", "HEAD" }, 20, ct);
            branch = symbolic.Success ? symbolic.StdOut.Trim() : "main";
        }

        return branch;
    }

    public async Task<bool> HasLocalChangesAsync(string repositoryPath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(repositoryPath,
            new[] { "status", "--porcelain=v2", "--untracked-files=normal" }, 30, ct);
        return result.Success && !string.IsNullOrWhiteSpace(result.StdOut);
    }

    public Task<GitResult> CloneAsync(string url, string targetDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
        return _runner.RunAsync(null, new[] { "clone", "--progress", url, targetDirectory }, 300, ct);
    }

    public Task<GitResult> FetchAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "fetch", "--prune", "--progress" }, 120, ct);

    public Task<GitResult> PullFastForwardAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "pull", "--ff-only", "--no-rebase" }, 120, ct);

    public Task<GitResult> StashAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "stash", "push", "--include-untracked" }, 60, ct);

    public Task<GitResult> StashPopAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "stash", "pop" }, 60, ct);

    public Task<GitResult> RebaseAbortAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "rebase", "--abort" }, 60, ct);

    public Task<GitResult> AddAsync(string repositoryPath, IEnumerable<string> paths, CancellationToken ct = default)
    {
        var arguments = new List<string> { "add", "--" };
        arguments.AddRange(paths);
        return _runner.RunAsync(repositoryPath, arguments, 60, ct);
    }

    /// <summary>Commit message goes through stdin so quotes and non-ASCII text are never mangled by the shell.</summary>
    public Task<GitResult> CommitAsync(string repositoryPath, string message, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "commit", "-F", "-" }, message, 120, ct);

    public Task<GitResult> PushAsync(string repositoryPath, CancellationToken ct = default)
        => _runner.RunAsync(repositoryPath, new[] { "push", "--progress" }, 180, ct);

    public static string? FindReadme(string repositoryPath)
    {
        var entries = Directory.EnumerateFiles(repositoryPath)
            .Where(file => Path.GetFileName(file).StartsWith("readme", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var extension = Path.GetExtension(file);
                return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                       || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                       || extension.Equals(".mdown", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var preferred in ReadmeNames)
        {
            var exact = entries.FirstOrDefault(file =>
                string.Equals(Path.GetFileName(file), preferred, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        return entries.FirstOrDefault();
    }

    public SemaphoreSlim LockFor(string repositoryPath)
    {
        var key = Path.GetFullPath(repositoryPath);
        lock (_locks)
        {
            if (!_locks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _locks[key] = gate;
            }

            return gate;
        }
    }
}
