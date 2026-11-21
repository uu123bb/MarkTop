using System.Text.RegularExpressions;

namespace MarkTop.Core.Git;

public enum GitErrorKind
{
    None,
    GitMissing,
    Auth,
    RepositoryNotFound,
    Network,
    NonFastForward,
    DirtyWorkTree,
    Conflict,
    Unknown,
}

public sealed record GitError(GitErrorKind Kind, string Message);

public static partial class GitErrors
{
    [GeneratedRegex("rejected|non-fast-forward|fetch first|Updates were rejected", RegexOptions.IgnoreCase)]
    private static partial Regex NonFastForwardRegex();

    [GeneratedRegex("Authentication failed|could not read Username|Permission denied \\(publickey\\)|terminal prompts disabled|HTTP 403|The requested URL returned error: 403", RegexOptions.IgnoreCase)]
    private static partial Regex AuthRegex();

    [GeneratedRegex("repository not found|not found|HTTP 404|does not appear to be a git repository", RegexOptions.IgnoreCase)]
    private static partial Regex NotFoundRegex();

    [GeneratedRegex("Could not resolve host|Failed to connect|Connection timed out|unable to access|Connection refused|network is unreachable", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkRegex();

    [GeneratedRegex("CONFLICT|conflict|Automatic merge failed|needs merge", RegexOptions.IgnoreCase)]
    private static partial Regex ConflictRegex();

    [GeneratedRegex("Your local changes|would be overwritten|Please commit your changes or stash them", RegexOptions.IgnoreCase)]
    private static partial Regex DirtyRegex();

    public static GitError Classify(GitResult result)
    {
        if (result.Success)
        {
            return new GitError(GitErrorKind.None, string.Empty);
        }

        if (result.TimedOut)
        {
            return new GitError(GitErrorKind.Network, "git 操作超时，请检查网络或代理设置。");
        }

        var text = result.Combined;
        if (AuthRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.Auth,
                "认证失败。HTTPS 仓库需要 Git Credential Manager 或 Personal Access Token，SSH 仓库需要可用的密钥。");
        }

        if (NotFoundRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.RepositoryNotFound,
                "仓库不存在或没有访问权限。请检查 GitHub 用户名拼写；私有仓库需要先完成认证。");
        }

        if (NonFastForwardRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.NonFastForward, "远端已有新的提交，需要先同步。");
        }

        if (ConflictRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.Conflict, "合并/变基产生冲突，需要手动解决。");
        }

        if (DirtyRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.DirtyWorkTree, "工作区有未提交的改动，操作被拒绝。");
        }

        if (NetworkRegex().IsMatch(text))
        {
            return new GitError(GitErrorKind.Network, "无法连接远端，请检查网络、代理或防火墙。");
        }

        return new GitError(GitErrorKind.Unknown, string.IsNullOrWhiteSpace(text) ? "git 执行失败。" : text.Trim());
    }
}
