using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace MarkTop.App.Web;

/// <summary>
/// Extracts the embedded web bundle into a content-hashed folder under %LOCALAPPDATA%\MarkTop\web.
/// Nothing is ever written next to the executable, which keeps the app portable-ish and
/// avoids failing when the exe lives in a read-only folder.
/// </summary>
public static class WebAssetInstaller
{
    public const string ResourceName = "MarkTop.web-assets.zip";

    public static string EnsureInstalled(out string? warning)
    {
        warning = null;
        var assembly = Assembly.GetExecutingAssembly();
        using var resource = assembly.GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            var devPath = LocateDevelopmentAssets();
            if (devPath is not null)
            {
                warning = "使用开发目录中的前端产物（未嵌入资源）。";
                return devPath;
            }

            throw new InvalidOperationException(
                $"嵌入资源 {ResourceName} 缺失，且找不到开发目录的前端产物。请重新构建。");
        }

        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        memory.Position = 0;

        var hash = Convert.ToHexString(SHA256.HashData(memory)).ToLowerInvariant()[..12];
        memory.Position = 0;

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkTop", "web");
        var target = Path.Combine(root, hash);

        if (IsUsable(target))
        {
            return target;
        }

        try
        {
            Extract(memory, root, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to TEMP for locked-down profiles.
            var tempRoot = Path.Combine(Path.GetTempPath(), "MarkTop", "web");
            warning = $"无法写入 %LOCALAPPDATA%，已改用临时目录（{ex.Message}）。";
            Extract(memory, tempRoot, Path.Combine(tempRoot, hash));
            return Path.Combine(tempRoot, hash);
        }

        CleanupOldVersions(root, hash);
        return target;
    }

    private static void Extract(Stream zipStream, string root, string target)
    {
        Directory.CreateDirectory(root);
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(temp);

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var destination = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                if (!WebPathGuard.IsInside(temp, destination))
                {
                    throw new InvalidOperationException($"压缩包包含非法路径：{entry.FullName}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(temp, target);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private static bool IsUsable(string directory) =>
        Directory.Exists(directory) && File.Exists(Path.Combine(directory, "index.html"));

    private static void CleanupOldVersions(string root, string currentHash)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(Path.GetFileName(directory), currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // in use by another instance, try again next launch
                }
            }
        }
        catch
        {
            // cleanup is best effort
        }
    }

    /// <summary>Lets `dotnet run` work without embedding: finds the built web dist folder in the repo.</summary>
    private static string? LocateDevelopmentAssets()
    {
        var candidates = new List<string>();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && directory is not null; i++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "src", "MarkTop.Web", "dist"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "web"));
        return candidates.FirstOrDefault(IsUsable);
    }
}

internal static class WebPathGuard
{
    public static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
