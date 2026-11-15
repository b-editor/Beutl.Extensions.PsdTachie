using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>Reads one character's part folders, either at the ZIP root or inside a character folder.</summary>
internal static class TachieZipReader
{
    private static readonly string[] s_folders = ["後", "体", "顔", "髪", "眉", "目", "口", "他", "全"];
    private const long MaxStoredBytes = 256L * 1024 * 1024;
    private const long MaxCanvasPixels = 64L * 1024 * 1024;

    public static PsdDocument Read(string path)
    {
        // ZipArchive decodes flagged UTF-8 itself. Latin-1 preserves the original bytes of unflagged
        // names, which Japanese archivers write in either Windows-932 or UTF-8.
        using var archive = ZipFile.Open(path, ZipArchiveMode.Read, Encoding.Latin1);
        var parts = s_folders.ToDictionary(folder => folder, _ => new List<PsdLayer>());
        string? characterFolder = null, presets = null;
        int width = 0, height = 0;
        long storedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string pathInZip = entry.FullName;
            if (pathInZip.All(c => c <= byte.MaxValue))
                pathInZip = JapaneseText.Decode(Encoding.Latin1.GetBytes(pathInZip));
            string[] segments = pathInZip.Replace('\\', '/').Split('/');
            if (segments.Length < 2 || segments.Contains("__MACOSX") || segments[^1].StartsWith('.')
                || !parts.TryGetValue(segments[^2], out List<PsdLayer>? group))
                continue;

            string file = segments[^1];
            bool isPreset = segments[^2] == "全" && file == "セット.txt";
            if (!isPreset && !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;

            string parent = string.Join("/", segments, 0, segments.Length - 2);
            characterFolder ??= parent;
            if (parent != characterFolder)
                throw new InvalidDataException("複数の立ち絵が見つかりました。1人分のパーツをまとめたZIPを選んでください。");

            if (entry.Length > MaxStoredBytes - storedBytes || (isPreset && entry.Length > 1024 * 1024))
                throw new InvalidDataException("ZIP内の立ち絵データが大きすぎます。");
            storedBytes += entry.Length;
            var data = new byte[(int)entry.Length];
            using Stream stream = entry.Open();
            stream.ReadExactly(data);
            if (isPreset)
            {
                presets = JapaneseText.Decode(data).TrimStart('\uFEFF');
                continue;
            }

            using var encoded = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(encoded);
            if (codec == null || codec.EncodedFormat != SKEncodedImageFormat.Png)
                throw new InvalidDataException($"PNGを読み込めませんでした: {pathInZip}");
            SKImageInfo info = codec.Info;
            width = Math.Max(width, info.Width);
            height = Math.Max(height, info.Height);
            if ((long)width * height > MaxCanvasPixels)
                throw new InvalidDataException("立ち絵の画像サイズが大きすぎます。");

            string folder = segments[^2];
            string name = Path.GetFileNameWithoutExtension(file);
            bool overlay = folder is "後" or "他" || (folder == "髪" && name == "01");
            group.Add(new PsdLayer(overlay ? name : "*" + name, false)
            {
                Width = info.Width,
                Height = info.Height,
                PngData = data,
                BlendModeKey = folder == "顔" && name.EndsWith('b') ? "mul " : "norm",
            });
        }

        if (width == 0 || height == 0)
            throw new InvalidDataException("ZIPの中に「体」「目」「口」などのパーツフォルダとPNGが見つかりませんでした。");

        var root = new PsdLayer("", true);
        bool wholeOnly = parts.Where(p => p.Key != "全").All(p => p.Value.Count == 0);
        foreach (string folder in s_folders)
        {
            if (parts[folder].Count == 0)
                continue;
            var group = new PsdLayer(folder, true) { IsVisibleByDefault = true };
            root.AddChild(group);
            // The tree shows children in reverse paint order. Put 00 at the top of each folder.
            foreach (PsdLayer layer in parts[folder].OrderByDescending(l => l.DisplayName, StringComparer.Ordinal))
            {
                layer.Left = (width - layer.Width) / 2;
                layer.Top = (height - layer.Height) / 2;
                group.AddChild(layer);
            }

            PsdLayer? initial = folder switch
            {
                "全" when !wholeOnly => null,
                "後" or "他" => group.Children.FirstOrDefault(l => l.DisplayName == "00"),
                _ => group.Children[^1],
            };
            if (initial != null)
                initial.IsVisibleByDefault = true;
            if (folder == "髪" && group.Children.FirstOrDefault(l => l.DisplayName == "01") is { } fringe)
                fringe.IsVisibleByDefault = true;
        }

        return new PsdDocument(width, height, root) { Zip = new TachieZip(root, presets) };
    }
}
