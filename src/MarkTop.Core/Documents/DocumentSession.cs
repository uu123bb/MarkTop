namespace MarkTop.Core.Documents;

/// <summary>
/// A file opened for editing. Keeps the original encoding and line endings so a save is byte-faithful
/// for unchanged content.
/// </summary>
public sealed class DocumentSession
{
    private DocumentSession(string path, DocumentKind kind, DetectedEncoding encoding, string lineEnding, string text)
    {
        Path = path;
        Kind = kind;
        Encoding = encoding;
        LineEnding = lineEnding;
        OriginalText = text;
        LastWriteTimeUtc = TryGetLastWrite(path);
    }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public DocumentKind Kind { get; }

    public DetectedEncoding Encoding { get; }

    public bool IsLossyFallback => Encoding.LossyFallback;

    public string LineEnding { get; }

    public string OriginalText { get; }

    public bool IsReadOnly { get; private set; }

    public DateTime? LastWriteTimeUtc { get; private set; }

    public static DocumentSession Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(full);
        var detected = TextEncodingDetector.Detect(bytes);
        var text = TextEncodingDetector.Decode(bytes, detected, out _);
        var session = new DocumentSession(full, DocumentClassifier.Classify(full), detected, LineEndings.Detect(text), text);

        try
        {
            using var stream = File.Open(full, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException)
        {
            session.IsReadOnly = true;
        }
        catch (IOException)
        {
            // locked by another process: keep editable, save will surface the error.
        }

        return session;
    }

    public static DocumentSession Create(string path, string text = "")
    {
        var full = System.IO.Path.GetFullPath(path);
        var session = new DocumentSession(
            full,
            DocumentClassifier.Classify(full),
            new DetectedEncoding(new System.Text.UTF8Encoding(false), false, false),
            Environment.NewLine,
            text);
        return session;
    }

    /// <summary>Writes the text back with the session's encoding and line endings.</summary>
    public void Save(string text)
    {
        var normalized = LineEndings.Normalize(text, LineEnding);
        var bytes = TextEncodingDetector.Encode(normalized, Encoding);
        AtomicWrite(Path, bytes);
        LastWriteTimeUtc = TryGetLastWrite(Path);
        IsReadOnly = false;
    }

    public static void SaveAs(string path, string text, string lineEnding)
    {
        var normalized = LineEndings.Normalize(text, lineEnding);
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(normalized);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        AtomicWrite(path, bytes);
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    private static DateTime? TryGetLastWrite(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }
    }

    public bool ChangedOnDisk()
    {
        var current = TryGetLastWrite(Path);
        return current is not null && LastWriteTimeUtc is not null && current != LastWriteTimeUtc;
    }
}
