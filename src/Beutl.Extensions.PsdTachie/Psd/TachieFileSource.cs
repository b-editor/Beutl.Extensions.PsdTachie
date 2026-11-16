using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.IO;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>A reference to a PSD, PSB or parts ZIP. The document cache opens it when needed.</summary>
[JsonConverter(typeof(TachieFileSourceJsonConverter))]
public sealed class TachieFileSource() : IFileSource, INotifyEdited
{
    private Uri? _uri;

    public TachieFileSource(Uri uri) : this() => ReadFrom(uri);

    public Uri Uri => _uri ?? throw new InvalidOperationException("No character file has been selected.");

    // The renderer checks this path every frame, so decode it once per selection or relink.
    internal string? LocalPath { get; private set; }

    public event EventHandler? Edited;

    public void ReadFrom(Uri uri)
    {
        if (uri is not { IsAbsoluteUri: true, IsFile: true })
            throw new NotSupportedException("The character source must be a local file.");

        _uri = uri;
        LocalPath = uri.LocalPath;
        Edited?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class TachieFileSourceJsonConverter : FileSourceJsonConverter
{
    public override IFileSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // FileInfo saved raw absolute paths. In those names '#' and '%XX' are literal characters.
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } path
            && (path.StartsWith('/') || path.StartsWith(@"\\", StringComparison.Ordinal) || path is [_, ':', '/' or '\\', ..]))
        {
            string escaped = path.Replace("%", "%25");
            if (path.StartsWith('/'))
                escaped = escaped.Replace("\\", "%5C"); // A backslash can be part of a Unix filename.
            return new TachieFileSource(new UriBuilder(Uri.UriSchemeFile, "") { Path = escaped }.Uri);
        }

        return base.Read(ref reader, typeToConvert, options);
    }
}
