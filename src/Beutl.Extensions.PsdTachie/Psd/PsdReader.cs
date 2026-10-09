using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>Reads layered PSD and PSB files (RGB or grayscale, 8/16/32 bits per channel).</summary>
public static class PsdReader
{
    private const short UserMaskChannel = -2;

    // Additional layer information keys whose length field is 8 bytes wide in PSB files.
    private static readonly HashSet<string> s_psbLongLengthKeys =
    [
        "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD"
    ];

    static PsdReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static PsdDocument Read(string path)
    {
        return Read(File.ReadAllBytes(path));
    }

    public static PsdDocument Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    public static PsdDocument Read(byte[] data)
    {
        var r = new Reader(data);
        if (r.ReadAscii(4) != "8BPS")
            throw new InvalidDataException("PSDファイルではありません。");

        ushort version = r.ReadUInt16();
        if (version is not (1 or 2))
            throw new InvalidDataException($"対応していないPSDのバージョンです: {version}");

        bool isPsb = version == 2;
        r.Skip(6);
        int channelCount = r.ReadUInt16();
        int height = checked((int)r.ReadUInt32());
        int width = checked((int)r.ReadUInt32());
        int depth = r.ReadUInt16();
        int colorMode = r.ReadUInt16();

        if (depth is not (8 or 16 or 32))
            throw new NotSupportedException($"{depth}ビットのPSDには対応していません。8/16/32ビットで保存してください。");
        if (colorMode is not (1 or 3))
            throw new NotSupportedException("RGBまたはグレースケール以外のカラーモードのPSDには対応していません。");

        var ctx = new Context(isPsb, depth, colorMode, width, height);

        r.Skip(checked((int)r.ReadUInt32())); // Color mode data
        r.Skip(checked((int)r.ReadUInt32())); // Image resources

        long layerAndMaskLength = isPsb ? checked((long)r.ReadUInt64()) : r.ReadUInt32();
        long layerAndMaskEnd = r.Position + layerAndMaskLength;
        List<LayerRecord> records = [];

        if (layerAndMaskLength > 0)
        {
            long layerInfoLength = isPsb ? checked((long)r.ReadUInt64()) : r.ReadUInt32();
            long layerInfoStart = r.Position;
            if (layerInfoLength > 0)
            {
                records = ReadLayerInfo(r, ctx);
                r.Position = layerInfoStart + layerInfoLength;
            }
            else
            {
                // 16- and 32-bit documents keep their layers in an "Lr16"/"Lr32" global block instead.
                r.Position = layerInfoStart;
                uint globalMaskLength = r.ReadUInt32();
                r.Skip(checked((int)globalMaskLength));
                records = ReadGlobalLayerBlocks(r, ctx, layerAndMaskEnd);
            }
        }

        r.Position = layerAndMaskEnd;

        PsdLayer root = records.Count > 0
            ? BuildTree(records)
            : BuildFlatDocument(r, ctx, channelCount);

        var all = new List<PsdLayer>();
        AssignPaths(root, all);
        return new PsdDocument(width, height, root, all);
    }

    private static List<LayerRecord> ReadGlobalLayerBlocks(Reader r, Context ctx, long end)
    {
        while (r.Position + 12 <= end)
        {
            if (!TryAlignToSignature(r, end))
                break;

            r.Skip(4);
            string key = r.ReadAscii(4);
            long length = ctx.IsPsb && s_psbLongLengthKeys.Contains(key) ? checked((long)r.ReadUInt64()) : r.ReadUInt32();
            long dataStart = r.Position;
            if (key is "Lr16" or "Lr32" or "Layr")
            {
                return ReadLayerInfo(r, ctx);
            }

            r.Position = dataStart + length;
        }

        return [];
    }

    private static List<LayerRecord> ReadLayerInfo(Reader r, Context ctx)
    {
        int count = Math.Abs((int)r.ReadInt16());
        var records = new List<LayerRecord>(count);
        for (int i = 0; i < count; i++)
        {
            records.Add(ReadLayerRecord(r, ctx));
        }

        foreach (LayerRecord record in records)
        {
            foreach (PsdChannel channel in record.Channels)
            {
                ReadChannelImage(r, ctx, record, channel);
            }
        }

        return records;
    }

    private static LayerRecord ReadLayerRecord(Reader r, Context ctx)
    {
        var record = new LayerRecord
        {
            Top = r.ReadInt32(),
            Left = r.ReadInt32(),
            Bottom = r.ReadInt32(),
            Right = r.ReadInt32(),
        };

        int channels = r.ReadUInt16();
        for (int i = 0; i < channels; i++)
        {
            short id = r.ReadInt16();
            long length = ctx.IsPsb ? checked((long)r.ReadUInt64()) : r.ReadUInt32();
            record.Channels.Add(new PsdChannel(id, length));
        }

        if (r.ReadAscii(4) != "8BIM")
            throw new InvalidDataException("レイヤーレコードの署名が不正です。");

        record.BlendModeKey = r.ReadAscii(4);
        record.Opacity = r.ReadByte();
        record.IsClipped = r.ReadByte() != 0;
        byte flags = r.ReadByte();
        record.IsHidden = (flags & 0x02) != 0;
        r.Skip(1);

        long extraLength = r.ReadUInt32();
        long extraEnd = r.Position + extraLength;

        uint maskLength = r.ReadUInt32();
        long maskEnd = r.Position + maskLength;
        if (maskLength >= 18)
        {
            record.MaskTop = r.ReadInt32();
            record.MaskLeft = r.ReadInt32();
            record.MaskBottom = r.ReadInt32();
            record.MaskRight = r.ReadInt32();
            record.MaskDefaultColor = r.ReadByte();
            byte maskFlags = r.ReadByte();
            record.MaskDisabled = (maskFlags & 0x02) != 0;
            record.HasMask = true;
        }

        r.Position = maskEnd;
        r.Skip(checked((int)r.ReadUInt32())); // Blending ranges

        int nameLength = r.ReadByte();
        byte[] nameBytes = r.ReadBytes(nameLength);
        record.Name = DecodeLegacyName(nameBytes);
        int padded = (1 + nameLength + 3) / 4 * 4;
        r.Skip(padded - 1 - nameLength);

        while (r.Position + 12 <= extraEnd)
        {
            if (!TryAlignToSignature(r, extraEnd))
                break;

            r.Skip(4);
            string key = r.ReadAscii(4);
            long length = ctx.IsPsb && s_psbLongLengthKeys.Contains(key) ? checked((long)r.ReadUInt64()) : r.ReadUInt32();
            long dataStart = r.Position;
            switch (key)
            {
                case "luni" when length >= 4:
                    int chars = checked((int)r.ReadUInt32());
                    string unicodeName = Encoding.BigEndianUnicode.GetString(r.ReadBytes(chars * 2)).TrimEnd('\0');
                    if (unicodeName.Length > 0)
                        record.Name = unicodeName;
                    break;
                case "lsct" or "lsdk" when length >= 4:
                    record.SectionType = checked((int)r.ReadUInt32());
                    if (length >= 12)
                    {
                        r.Skip(4);
                        record.SectionBlendModeKey = r.ReadAscii(4);
                    }

                    break;
                case "iOpa" when length >= 1:
                    record.FillOpacity = r.ReadByte();
                    break;
            }

            r.Position = Math.Min(dataStart + length, extraEnd);
        }

        r.Position = extraEnd;
        return record;
    }

    private static void ReadChannelImage(Reader r, Context ctx, LayerRecord record, PsdChannel channel)
    {
        long start = r.Position;
        long end = start + channel.Length;
        try
        {
            if (channel.Length < 2)
                return;

            int compression = r.ReadUInt16();
            int width;
            int height;
            if (channel.Id == UserMaskChannel)
            {
                width = record.MaskRight - record.MaskLeft;
                height = record.MaskBottom - record.MaskTop;
            }
            else if (channel.Id < UserMaskChannel)
            {
                return; // Real user masks and vector data are not rendered.
            }
            else
            {
                width = record.Right - record.Left;
                height = record.Bottom - record.Top;
            }

            if (width <= 0 || height <= 0)
                return;

            int rowBytes = width * ctx.Depth / 8;
            byte[] raw = DecodeChannel(r, ctx, compression, rowBytes, height, end);
            channel.Data = NormalizeTo8Bit(raw, width, height, ctx.Depth);
        }
        finally
        {
            r.Position = end;
        }
    }

    private static byte[] DecodeChannel(Reader r, Context ctx, int compression, int rowBytes, int height, long end)
    {
        switch (compression)
        {
            case 0:
                return r.ReadBytes(checked(rowBytes * height));
            case 1:
                {
                    var rowLengths = new int[height];
                    for (int y = 0; y < height; y++)
                    {
                        rowLengths[y] = ctx.IsPsb ? checked((int)r.ReadUInt32()) : r.ReadUInt16();
                    }

                    var output = new byte[checked(rowBytes * height)];
                    for (int y = 0; y < height; y++)
                    {
                        UnpackBits(r.ReadSpan(rowLengths[y]), output.AsSpan(y * rowBytes, rowBytes));
                    }

                    return output;
                }
            case 2 or 3:
                {
                    byte[] compressed = r.ReadBytes(checked((int)(end - r.Position)));
                    var output = new byte[checked(rowBytes * height)];
                    using (var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
                    {
                        zlib.ReadAtLeast(output, output.Length, throwOnEndOfStream: false);
                    }

                    if (compression == 3)
                        UndoPrediction(output, rowBytes, height, ctx.Depth);
                    return output;
                }
            default:
                throw new NotSupportedException($"対応していない圧縮形式です: {compression}");
        }
    }

    internal static void UnpackBits(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int si = 0;
        int di = 0;
        while (si < source.Length && di < destination.Length)
        {
            int n = (sbyte)source[si++];
            if (n >= 0)
            {
                int count = Math.Min(n + 1, Math.Min(source.Length - si, destination.Length - di));
                source.Slice(si, count).CopyTo(destination.Slice(di, count));
                si += n + 1;
                di += count;
            }
            else if (n != -128)
            {
                if (si >= source.Length)
                    break;
                byte value = source[si++];
                int count = Math.Min(1 - n, destination.Length - di);
                destination.Slice(di, count).Fill(value);
                di += count;
            }
        }
    }

    private static void UndoPrediction(byte[] data, int rowBytes, int height, int depth)
    {
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = data.AsSpan(y * rowBytes, rowBytes);
            switch (depth)
            {
                case 8:
                    for (int i = 1; i < row.Length; i++)
                        row[i] += row[i - 1];
                    break;
                case 16:
                    for (int i = 2; i + 1 < row.Length; i += 2)
                    {
                        ushort previous = BinaryPrimitives.ReadUInt16BigEndian(row[(i - 2)..]);
                        ushort delta = BinaryPrimitives.ReadUInt16BigEndian(row[i..]);
                        BinaryPrimitives.WriteUInt16BigEndian(row[i..], (ushort)(previous + delta));
                    }

                    break;
                case 32:
                    {
                        for (int i = 1; i < row.Length; i++)
                            row[i] += row[i - 1];

                        // The four bytes of each float are stored as four planes of the row.
                        int width = rowBytes / 4;
                        byte[] planar = row.ToArray();
                        for (int x = 0; x < width; x++)
                        {
                            row[x * 4] = planar[x];
                            row[x * 4 + 1] = planar[width + x];
                            row[x * 4 + 2] = planar[width * 2 + x];
                            row[x * 4 + 3] = planar[width * 3 + x];
                        }

                        break;
                    }
            }
        }
    }

    private static byte[] NormalizeTo8Bit(byte[] raw, int width, int height, int depth)
    {
        int count = width * height;
        switch (depth)
        {
            case 8:
                return raw;
            case 16:
                {
                    var result = new byte[count];
                    for (int i = 0; i < count && i * 2 < raw.Length; i++)
                        result[i] = raw[i * 2];
                    return result;
                }
            default:
                {
                    var result = new byte[count];
                    for (int i = 0; i < count && i * 4 + 3 < raw.Length; i++)
                    {
                        float value = BinaryPrimitives.ReadSingleBigEndian(raw.AsSpan(i * 4));
                        // 32-bit documents store linear light; encode with the sRGB curve.
                        float linear = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
                        float encoded = linear <= 0.0031308f
                            ? linear * 12.92f
                            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
                        result[i] = (byte)Math.Clamp(MathF.Round(encoded * 255f), 0f, 255f);
                    }

                    return result;
                }
        }
    }

    private static PsdLayer BuildFlatDocument(Reader r, Context ctx, int channelCount)
    {
        var root = new PsdLayer("", true) { IsVisibleByDefault = true };
        if (r.Remaining < 2)
            return root;

        int compression = r.ReadUInt16();
        int rowBytes = ctx.Width * ctx.Depth / 8;
        int planes = Math.Min(channelCount, ctx.ColorMode == 3 ? 4 : 2);
        var layer = new PsdLayer("背景", false)
        {
            IsVisibleByDefault = true,
            Width = ctx.Width,
            Height = ctx.Height,
        };

        int[]? rowLengths = null;
        if (compression == 1)
        {
            // All scanline lengths of every channel come first, then the packed data.
            rowLengths = new int[channelCount * ctx.Height];
            for (int i = 0; i < rowLengths.Length; i++)
                rowLengths[i] = ctx.IsPsb ? checked((int)r.ReadUInt32()) : r.ReadUInt16();
        }
        else if (compression != 0)
        {
            throw new NotSupportedException($"統合画像の圧縮形式に対応していません: {compression}");
        }

        int colorPlanes = ctx.ColorMode == 3 ? 3 : 1;
        for (int c = 0; c < planes; c++)
        {
            var raw = new byte[checked(rowBytes * ctx.Height)];
            if (rowLengths == null)
            {
                r.ReadSpan(raw.Length).CopyTo(raw);
            }
            else
            {
                for (int y = 0; y < ctx.Height; y++)
                    UnpackBits(r.ReadSpan(rowLengths[c * ctx.Height + y]), raw.AsSpan(y * rowBytes, rowBytes));
            }

            short id = c < colorPlanes ? (short)c : (short)-1;
            layer.Channels.Add(new PsdChannel(id, raw.Length) { Data = NormalizeTo8Bit(raw, ctx.Width, ctx.Height, ctx.Depth) });
        }

        root.AddChild(layer);
        return root;
    }

    private static PsdLayer BuildTree(List<LayerRecord> records)
    {
        var root = new PsdLayer("", true) { IsVisibleByDefault = true };
        var stack = new Stack<PsdLayer>();
        stack.Push(root);

        foreach (LayerRecord record in records)
        {
            switch (record.SectionType)
            {
                case 3:
                    {
                        // A bounding divider opens a group from its bottom; the folder record closes it.
                        var group = new PsdLayer("", true);
                        stack.Peek().AddChild(group);
                        stack.Push(group);
                        break;
                    }
                case 1 or 2:
                    {
                        PsdLayer group;
                        if (stack.Count > 1)
                        {
                            group = stack.Pop();
                        }
                        else
                        {
                            group = new PsdLayer("", true);
                            root.AddChild(group);
                        }

                        Apply(record, group);
                        group.BlendModeKey = record.SectionBlendModeKey ?? record.BlendModeKey;
                        break;
                    }
                default:
                    {
                        var layer = new PsdLayer(record.Name, false);
                        Apply(record, layer);
                        layer.Channels.AddRange(record.Channels);
                        stack.Peek().AddChild(layer);
                        break;
                    }
            }
        }

        return root;
    }

    private static void Apply(LayerRecord record, PsdLayer layer)
    {
        layer.Name = record.Name;
        layer.IsVisibleByDefault = !record.IsHidden;
        layer.Opacity = record.Opacity;
        layer.FillOpacity = record.FillOpacity;
        layer.BlendModeKey = record.BlendModeKey;
        layer.IsClipped = record.IsClipped;
        layer.Left = record.Left;
        layer.Top = record.Top;
        layer.Width = Math.Max(0, record.Right - record.Left);
        layer.Height = Math.Max(0, record.Bottom - record.Top);

        if (record.HasMask && !record.MaskDisabled)
        {
            int maskWidth = record.MaskRight - record.MaskLeft;
            int maskHeight = record.MaskBottom - record.MaskTop;
            byte[]? pixels = record.Channels.FirstOrDefault(c => c.Id == UserMaskChannel)?.Data;
            if (maskWidth > 0 && maskHeight > 0 && pixels != null)
            {
                layer.Mask = new PsdLayerMask(record.MaskLeft, record.MaskTop, maskWidth, maskHeight,
                    record.MaskDefaultColor, pixels);
            }
            else if (record.MaskDefaultColor == 0 && maskWidth <= 0)
            {
                // An empty mask that hides everything.
                layer.Mask = new PsdLayerMask(0, 0, 0, 0, 0, []);
            }
        }
    }

    private static void AssignPaths(PsdLayer root, List<PsdLayer> all)
    {
        Visit(root, "");

        void Visit(PsdLayer parent, string prefix)
        {
            // Number duplicates from the top, the order the layer panel shows them in.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var paths = new string[parent.Children.Count];
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                string escaped = EscapeName(parent.Children[i].Name);
                int n = seen.TryGetValue(escaped, out int count) ? count + 1 : 1;
                seen[escaped] = n;
                paths[i] = prefix + (n == 1 ? escaped : $"{escaped}#{n}");
            }

            for (int i = 0; i < parent.Children.Count; i++)
            {
                PsdLayer child = parent.Children[i];
                child.Path = paths[i];
                child.Index = all.Count;
                all.Add(child);
                if (child.IsGroup)
                    Visit(child, child.Path + "/");
            }
        }
    }

    internal static string EscapeName(string name)
    {
        return name.Replace("\\", "\\\\").Replace("/", "\\/").Replace("#", "\\#");
    }

    private static string DecodeLegacyName(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Japanese Photoshop writes legacy names in Shift_JIS.
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    private static bool TryAlignToSignature(Reader r, long end)
    {
        // Writers disagree on padding additional-information blocks to 2 or 4 bytes.
        for (int i = 0; i < 4 && r.Position + 4 <= end; i++)
        {
            if (r.PeekAscii(4) is "8BIM" or "8B64")
                return true;
            r.Skip(1);
        }

        return false;
    }

    private sealed record Context(bool IsPsb, int Depth, int ColorMode, int Width, int Height);

    private sealed class LayerRecord
    {
        public int Top;
        public int Left;
        public int Bottom;
        public int Right;
        public readonly List<PsdChannel> Channels = [];
        public string BlendModeKey = "norm";
        public string? SectionBlendModeKey;
        public byte Opacity = 255;
        public byte FillOpacity = 255;
        public bool IsClipped;
        public bool IsHidden;
        public string Name = "";
        public int SectionType;
        public bool HasMask;
        public bool MaskDisabled;
        public int MaskTop;
        public int MaskLeft;
        public int MaskBottom;
        public int MaskRight;
        public byte MaskDefaultColor;
    }

    private sealed class Reader(byte[] data)
    {
        public long Position { get; set; }

        public long Remaining => data.Length - Position;

        public byte ReadByte() => ReadSpan(1)[0];

        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(ReadSpan(2));

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadSpan(2));

        public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadSpan(4));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadSpan(4));

        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadSpan(8));

        public string ReadAscii(int length) => Encoding.ASCII.GetString(ReadSpan(length));

        public string PeekAscii(int length) => Encoding.ASCII.GetString(data, checked((int)Position), length);

        public byte[] ReadBytes(int length) => ReadSpan(length).ToArray();

        public ReadOnlySpan<byte> ReadSpan(int length)
        {
            if (length < 0 || Position + length > data.Length)
                throw new EndOfStreamException("PSDファイルが途中で終わっています。");

            var span = new ReadOnlySpan<byte>(data, checked((int)Position), length);
            Position += length;
            return span;
        }

        public void Skip(int length)
        {
            if (length < 0 || Position + length > data.Length)
                throw new EndOfStreamException("PSDファイルが途中で終わっています。");
            Position += length;
        }
    }
}
