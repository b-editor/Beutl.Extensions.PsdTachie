using System.Diagnostics.CodeAnalysis;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using NAudio.Wave;

namespace Beutl.Extensions.PsdTachie.Tests;

/// <summary>
/// A WAV decoder for tests. The editor registers FFmpeg/AVFoundation decoders at startup; a headless test has
/// none, so audio-driven features need this one.
/// </summary>
internal sealed class TestWaveDecoder : IDecoderInfo
{
    public static readonly TestWaveDecoder Instance = new();

    private static int s_registered;

    public string Name => "Test WAV decoder";

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 0)
            DecoderRegistry.Register(Instance);
    }

    public MediaReader? Open(string file, MediaOptions options) => new Reader(file);

    public IEnumerable<string> VideoExtensions() => [];

    public IEnumerable<string> AudioExtensions() => [".wav"];

    /// <summary>Writes a mono 16-bit WAV file from samples in [-1, 1].</summary>
    public static void Write(string path, int sampleRate, IEnumerable<float> samples)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        foreach (float sample in samples)
            writer.WriteSample(sample);
    }

    private sealed class Reader : MediaReader
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _stereo;

        public Reader(string file)
        {
            _reader = new WaveFileReader(file);
            ISampleProvider samples = _reader.ToSampleProvider();
            _stereo = samples.WaveFormat.Channels == 1 ? samples.ToStereo() : samples;
            AudioInfo = new AudioStreamInfo(
                "pcm",
                new Rational(_reader.SampleCount, _reader.WaveFormat.SampleRate),
                _reader.WaveFormat.SampleRate,
                _reader.WaveFormat.Channels);
        }

        public override VideoStreamInfo VideoInfo => throw new NotSupportedException();

        public override AudioStreamInfo AudioInfo { get; }

        public override bool HasVideo => false;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            _reader.Position = Math.Min(_reader.Length, (long)start * _reader.WaveFormat.BlockAlign);
            sound = SampleProviderReader.ReadStereo(_stereo, _reader.WaveFormat.SampleRate, length);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _reader.Dispose();
        }
    }
}
