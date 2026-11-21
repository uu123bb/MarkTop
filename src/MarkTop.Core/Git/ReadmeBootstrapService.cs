namespace MarkTop.Core.Git;

public sealed record ProfileRepo(string Path, string Branch, string ReadmePath);

public sealed record BootstrapResult(
    bool Success,
    ProfileRepo? Repo,
    GitError? Error,
    IReadOnlyList<string> Warnings)
{
    public static BootstrapResult Fail(GitError error) => new(false, null, error, Array.Empty<string>());
}

public sealed record PublishResult(bool Committed, bool Pushed, GitError? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// Implements the "quick edit GitHub profile README" flow: clone or reuse
/// https://github.com/&lt;user&gt;/&lt;user&gt;.git, open its README, then commit and push on save.
/// </summary>
public sealed class ReadmeBootstrapService
{
    private readonly GitClient _git;
    private readonly string _cloneRoot;

    public ReadmeBootstrapService(GitClient git, string cloneRoot)
    {
        _git = git;
        _cloneRoot = string.IsNullOrWhiteSpace(cloneRoot)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkTop", "repos")
            : cloneRoot;
    }

    public GitClient Git => _git;

    public string CloneRoot => _cloneRoot;

    public string RepositoryPathFor(string user) => System.IO.Path.Combine(_cloneRoot, user, user);

    public string RemoteUrlFor(string user) => $"https://github.com/{user}/{user}.git";

    public async Task<BootstrapResult> PrepareAsync(
        string user,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        user = (user ?? string.Empty).Trim();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(user))
        {
            return BootstrapResult.Fail(new GitError(GitErrorKind.Unknown, "请填写 GitHub 用户名。"));
        }

        if (user.Contains('/') || user.Contains('\\') || user.Contains("..", StringComparison.Ordinal))
        {
            return BootstrapResult.Fail(new GitError(GitErrorKind.Unknown, "GitHub 用户名包含非法字符。"));
        }

        var verify = await _git.VerifyAsync(ct);
        if (!verify.Ok)
        {
            return BootstrapResult.Fail(new GitError(GitErrorKind.GitMissing, verify.Error ?? "git 不可用。"));
        }

        var (name, email) = await _git.GetIdentityAsync(ct);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            warnings.Add("git 未配置 user.name / user.email，提交可能失败，请在设置里补齐。");
        }

        var repoPath = RepositoryPathFor(user);
        var url = RemoteUrlFor(user);

        if (Directory.Exists(repoPath))
        {
            if (!await _git.IsRepositoryAsync(repoPath, ct))
            {
                return BootstrapResult.Fail(new GitError(GitErrorKind.Unknown,
                    $"目标目录已存在但不是 git 仓库：{repoPath}。请换一个 Clone 目录，或先手动处理该目录。"));
            }

            var remote = await _git.GetRemoteUrlAsync(repoPath, ct);
            if (!string.IsNullOrWhiteSpace(remote) &&
                !remote.EndsWith($"{user}/{user}.git", StringComparison.OrdinalIgnoreCase) &&
                !remote.EndsWith($"{user}/{user}", StringComparison.OrdinalIgnoreCase))
            {
                return BootstrapResult.Fail(new GitError(GitErrorKind.Unknown,
                    $"目录已被其他仓库占用（remote: {remote}），已停止操作。"));
            }

            progress?.Report("正在获取最新提交…");
            var fetch = await _git.FetchAsync(repoPath, ct);
            if (!fetch.Success)
            {
                warnings.Add($"拉取远端失败：{GitErrors.Classify(fetch).Message}");
            }
            else
            {
                if (await _git.HasLocalChangesAsync(repoPath, ct))
                {
                    warnings.Add("该仓库存在未提交的本地改动，已跳过自动拉取。");
                }
                else
                {
                    var pull = await _git.PullFastForwardAsync(repoPath, ct);
                    if (!pull.Success)
                    {
                        warnings.Add($"快进合并失败：{GitErrors.Classify(pull).Message}");
                    }
                }
            }
        }
        else
        {
            progress?.Report($"正在克隆 {url} …");
            var clone = await _git.CloneAsync(url, repoPath, ct);
            if (!clone.Success)
            {
                return BootstrapResult.Fail(GitErrors.Classify(clone));
            }
        }

        var branch = await _git.GetCurrentBranchAsync(repoPath, ct);
        var readme = GitClient.FindReadme(repoPath);
        if (readme is null)
        {
            readme = System.IO.Path.Combine(repoPath, "README.md");
            await File.WriteAllTextAsync(readme,
                $"# {user}{Environment.NewLine}{Environment.NewLine}在 MarkTop 里开始编辑这一行。{Environment.NewLine}", ct);
            warnings.Add("仓库里没有 README，已新建 README.md（尚未提交）。");
        }

        return new BootstrapResult(true, new ProfileRepo(repoPath, branch, readme), null, warnings);
    }

    /// <summary>
    /// Stages the README, commits with the user supplied message and pushes. Retries once with a rebase when
    /// the remote moved ahead.
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        ProfileRepo repo,
        string commitMessage,
        bool push,
        CancellationToken ct = default)
    {
        var gate = _git.LockFor(repo.Path);
        await gate.WaitAsync(ct);
        try
        {
            var relative = System.IO.Path.GetRelativePath(repo.Path, repo.ReadmePath).Replace('\\', '/');
            var add = await _git.AddAsync(repo.Path, new[] { relative }, ct);
            if (!add.Success)
            {
                return new PublishResult(false, false, GitErrors.Classify(add));
            }

            var message = string.IsNullOrWhiteSpace(commitMessage)
                ? Configuration.GitSettings.DefaultCommitMessage
                : commitMessage.Trim();

            var commit = await _git.CommitAsync(repo.Path, message, ct);
            var committed = true;
            if (!commit.Success)
            {
                var text = commit.Combined;
                var nothingToCommit = text.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
                                      || text.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase)
                                      || text.Contains("nothing added to commit", StringComparison.OrdinalIgnoreCase);
                if (!nothingToCommit)
                {
                    return new PublishResult(false, false, GitErrors.Classify(commit));
                }

                committed = false;
            }

            if (!push)
            {
                return new PublishResult(committed, false, null);
            }

            var pushResult = await _git.PushAsync(repo.Path, ct);
            if (pushResult.Success)
            {
                return new PublishResult(committed, true, null);
            }

            var error = GitErrors.Classify(pushResult);
            if (error.Kind != GitErrorKind.NonFastForward)
            {
                return new PublishResult(committed, false, error);
            }

            // Remote moved ahead: fetch, rebase the local commit on top, push again. At most two rounds.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var fetch = await _git.FetchAsync(repo.Path, ct);
                if (!fetch.Success)
                {
                    return new PublishResult(committed, false, GitErrors.Classify(fetch));
                }

                var rebase = await _git.Runner.RunAsync(repo.Path,
                    new[] { "rebase", $"origin/{repo.Branch}" }, 120, ct);
                if (!rebase.Success)
                {
                    var rebaseError = GitErrors.Classify(rebase);
                    if (rebaseError.Kind == GitErrorKind.Conflict)
                    {
                        await _git.RebaseAbortAsync(repo.Path, ct);
                        return new PublishResult(committed, false, new GitError(GitErrorKind.Conflict,
                            "远端 README 与本地修改冲突，已回滚变基。请手动解决冲突后再试。"));
                    }

                    return new PublishResult(committed, false, rebaseError);
                }

                pushResult = await _git.PushAsync(repo.Path, ct);
                if (pushResult.Success)
                {
                    return new PublishResult(committed, true, null);
                }

                if (GitErrors.Classify(pushResult).Kind != GitErrorKind.NonFastForward)
                {
                    break;
                }
            }

            return new PublishResult(committed, false, GitErrors.Classify(pushResult));
        }
        finally
        {
            gate.Release();
        }
    }
}
