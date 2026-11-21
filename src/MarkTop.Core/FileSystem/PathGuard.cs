using System.Security.Cryptography;

namespace MarkTop.Core.FileSystem;

public static class PathGuard
{
    /// <summary>True when <paramref name="candidate"/> resolves inside <paramref name="root"/>.</summary>
    public static bool IsWithin(string root, string candidate)
    {
        var rootFull = Path.GetFullPath(root);
        var candidateFull = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(rootFull.TrimEnd(Path.DirectorySeparatorChar), candidateFull.TrimEnd(Path.DirectorySeparatorChar), comparison))
        {
            return true;
        }

        return candidateFull.StartsWith(rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }
}

/// <summary>Stores pasted images next to the document in an assets folder and returns the relative path.</summary>
public static class AssetStore
{
    public const string AssetsFolderName = "assets";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
    };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static string SaveImage(string documentPath, string suggestedName, ReadOnlySpan<byte> content)
    {
        var documentDirectory = Path.GetDirectoryName(Path.GetFullPath(documentPath))
                                ?? throw new InvalidOperationException("文档没有所在目录。");
        var assetsDirectory = Path.Combine(documentDirectory, AssetsFolderName);
        Directory.CreateDirectory(assetsDirectory);

        var extension = Path.GetExtension(suggestedName);
        if (!ImageExtensions.Contains(extension))
        {
            extension = ".png";
        }

        var hash = Convert.ToHexString(SHA256.HashData(content))[..8].ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(suggestedName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image";
        }

        baseName = string.Join("-", baseName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var fileName = $"{baseName}-{hash}{extension}".ToLowerInvariant();
        var target = Path.Combine(assetsDirectory, fileName);

        if (!PathGuard.IsWithin(assetsDirectory, target))
        {
            throw new InvalidOperationException("图片路径越界。");
        }

        if (!File.Exists(target))
        {
            File.WriteAllBytes(target, content.ToArray());
        }

        return $"{AssetsFolderName}/{fileName}";
    }
}
