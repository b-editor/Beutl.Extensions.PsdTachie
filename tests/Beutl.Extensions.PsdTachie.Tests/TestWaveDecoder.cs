using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Media;
using Beutl.Media.Decoding;
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

    public static void Write(string path, int sampleRate, IEnumerable<float> samples, int channels = 1)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        foreach (float sample in samples)
            writer.WriteSample(sample);
    }

    private sealed class Reader : MediaReader
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _samples;

        public Reader(string file)
        {
            _reader = new WaveFileReader(file);
            _samples = _reader.ToSampleProvider();
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

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            _reader.Position = Math.Min(_reader.Length, (long)start * _reader.WaveFormat.BlockAlign);
            int channels = _samples.WaveFormat.Channels;
            var interleaved = new float[length * channels];
            int frames = _samples.Read(interleaved.AsSpan()) / channels;
            sound = new AudioBuffer(_reader.WaveFormat.SampleRate, channels, frames);
            for (int channel = 0; channel < channels; channel++)
            {
                Span<float> data = sound.GetChannelData(channel);
                for (int i = 0; i < frames; i++)
                    data[i] = interleaved[i * channels + channel];
            }
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
