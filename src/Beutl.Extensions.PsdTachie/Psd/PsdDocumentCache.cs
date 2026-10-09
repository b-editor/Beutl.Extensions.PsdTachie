using System.Collections.Concurrent;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>A parsed document and the compositor that owns its decoded layer bitmaps.</summary>
public sealed class LoadedPsd(PsdDocument document, string path, DateTime lastWriteTimeUtc)
{
    public PsdDocument Document { get; } = document;

    public PsdCompositor Compositor { get; } = new(document);

    public string Path { get; } = path;

    public DateTime LastWriteTimeUtc { get; } = lastWriteTimeUtc;
}

/// <summary>Shares parsed documents between the drawables and property editors that open the same file.</summary>
public static class PsdDocumentCache
{
    private static readonly ConcurrentDictionary<string, WeakReference<LoadedPsd>> s_cache = new(StringComparer.Ordinal);
    private static readonly Lock s_loadLock = new();

    /// <summary>Returns the parsed document, re-reading the file when it changed on disk.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid PSD.</exception>
    /// <exception cref="NotSupportedException">The PSD uses an unsupported color mode or depth.</exception>
    public static LoadedPsd Load(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        DateTime lastWrite = File.GetLastWriteTimeUtc(fullPath);
        if (TryGetCurrent(fullPath, lastWrite, out LoadedPsd? cached))
            return cached;

        lock (s_loadLock)
        {
            if (TryGetCurrent(fullPath, lastWrite, out cached))
                return cached;

            var loaded = new LoadedPsd(PsdReader.Read(fullPath), fullPath, lastWrite);
            s_cache[fullPath] = new WeakReference<LoadedPsd>(loaded);
            return loaded;
        }
    }

    private static bool TryGetCurrent(string fullPath, DateTime lastWrite, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LoadedPsd? loaded)
    {
        if (s_cache.TryGetValue(fullPath, out WeakReference<LoadedPsd>? weak)
            && weak.TryGetTarget(out loaded)
            && loaded.LastWriteTimeUtc == lastWrite)
        {
            return true;
        }

        loaded = null;
        return false;
    }
}
