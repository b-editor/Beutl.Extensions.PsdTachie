using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensions.PsdTachie.Animation;
using Beutl.Extensions.PsdTachie.Psd;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Extensions.PsdTachie.Graphics;

/// <summary>
/// Character art from a layered PSD. Layers can be switched individually, and the mouth and eye layers can
/// follow an audio track and a blink timeline.
/// </summary>
/// <remarks>The original duration is the lip-sync audio's, counted from the object's start.</remarks>
[Display(Name = "PSD立ち絵")]
public sealed partial class PsdTachieDrawable : Drawable, IOriginalDurationProvider, ISplittable
{
    public PsdTachieDrawable()
    {
        ScanProperties<PsdTachieDrawable>();
        // The layer tree assigns the mouth and eye layers, so these do not get rows of their own.
        HideProperties(MouthClosed, MouthHalfOpen, MouthOpen, EyeOpen, EyeHalfOpen, EyeClosed);
    }

    [Display(Name = "PSDファイル")]
    public IProperty<FileInfo?> Source { get; } = Property.Create<FileInfo?>();

    [PsdLayerState]
    [Display(Name = "レイヤー")]
    public IProperty<string> Layers { get; } = Property.Create("");

    [Display(Name = "口パクの音声", Description = "この音声の音量に合わせて口のレイヤーを切り替えます")]
    public IProperty<SoundSource?> LipSyncAudio { get; } = Property.Create<SoundSource?>();

    [Display(Name = "音声の開始位置", Description = "オブジェクトの先頭から音声が始まるまでの時間")]
    public IProperty<TimeSpan> LipSyncOffset { get; } = Property.Create(TimeSpan.Zero);

    [Display(Name = "閉じた口")]
    public IProperty<string> MouthClosed { get; } = Property.Create("");

    [Display(Name = "半開きの口")]
    public IProperty<string> MouthHalfOpen { get; } = Property.Create("");

    [Display(Name = "開いた口")]
    public IProperty<string> MouthOpen { get; } = Property.Create("");

    [Range(-96f, 0f)]
    [Display(Name = "半開きにする音量 (dB)")]
    public IProperty<float> HalfOpenThreshold { get; } = Property.CreateAnimatable(-36f);

    [Range(-96f, 0f)]
    [Display(Name = "開く音量 (dB)")]
    public IProperty<float> OpenThreshold { get; } = Property.CreateAnimatable(-24f);

    [Display(Name = "開いた目")]
    public IProperty<string> EyeOpen { get; } = Property.Create("");

    [Display(Name = "半目")]
    public IProperty<string> EyeHalfOpen { get; } = Property.Create("");

    [Display(Name = "閉じた目")]
    public IProperty<string> EyeClosed { get; } = Property.Create("");

    [Range(0.1f, 60f)]
    [Display(Name = "まばたきの間隔 (秒)")]
    public IProperty<float> BlinkInterval { get; } = Property.Create(4f);

    [Range(0f, 100f)]
    [Display(Name = "間隔のばらつき (%)")]
    public IProperty<float> BlinkRandomness { get; } = Property.Create(50f);

    [Range(0.01f, 2f)]
    [Display(Name = "まばたきの長さ (秒)")]
    public IProperty<float> BlinkDuration { get; } = Property.Create(0.15f);

    [Display(Name = "まばたきの乱数シード")]
    public IProperty<int> BlinkSeed { get; } = Property.Create(0);

    public bool HasOriginalDuration()
    {
        return LipSyncAudio.CurrentValue != null;
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        using SoundSource.Resource? audio = LipSyncAudio.CurrentValue?.ToResource(CompositionContext.Default);
        timeSpan = audio != null ? LipSyncOffset.CurrentValue + audio.Duration : TimeSpan.Zero;
        return audio is { Duration.Ticks: > 0 } && timeSpan > TimeSpan.Zero;
    }

    /// <summary>
    /// Keeps the mouth on the voice when the element is split: the back half starts later, so its voice starts earlier
    /// by as much, as a split sound does. Blinks start over in the back half, with the eyes open.
    /// </summary>
    public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
    {
        if (backward)
            LipSyncOffset.CurrentValue -= startDelta;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return r.Loaded?.Document is { } document
            ? new Size(document.Width, document.Height)
            : default;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Frame is { IsDisposed: false } frame)
        {
            context.DrawNode(
                frame,
                static bitmap => new PsdBitmapRenderNode(bitmap),
                static (node, bitmap) => node.Update(bitmap));
        }
    }

    public partial class Resource
    {
        // A short hold keeps the mouth from flickering between syllables.
        private static readonly TimeSpan s_levelHold = TimeSpan.FromMilliseconds(40);

        private readonly PsdFrameCache _frames = new();
        private LoudnessEnvelope? _envelope;
        private LoadedPsd? _framesOwner;
        private string? _frameKey;
        private string? _loadedPath;
        private DateTime _loadedWriteTime;

        internal Bitmap? Frame { get; private set; }

        internal LoadedPsd? Loaded { get; private set; }

        partial void PostUpdate(PsdTachieDrawable obj, CompositionContext context)
        {
            UpdateDocument();
            LoadedPsd? loaded = Loaded;
            if (!ReferenceEquals(loaded, _framesOwner))
            {
                _frames.Clear();
                _framesOwner = loaded;
            }

            string? key = null;
            Bitmap? frame = null;
            if (loaded != null)
            {
                TimeSpan localTime = context.Time - obj.TimeRange.Start;
                Dictionary<PsdLayer, bool> choices = ChooseShapes(loaded.Document, localTime);
                bool[] flags = PsdLayerState.Resolve(loaded.Document, Layers, choices);
                key = PsdLayerState.CreateCacheKey(flags);
                frame = _frames.GetOrCreate(key, () => new Bitmap(loaded.Compositor.Compose(flags)));
            }

            if (key != _frameKey || !ReferenceEquals(frame, Frame))
            {
                _frameKey = key;
                Frame = frame;
                Version++;
            }

            _frames.Tick();
        }

        partial void PostDispose(bool disposing)
        {
            Frame = null;
            Loaded = null;
            _frames.Dispose();
            _envelope = null;
        }

        private void UpdateDocument()
        {
            // Reload when another file is chosen or the file changes on disk.
            string? path = Source?.FullName;
            DateTime writeTime = path != null ? GetWriteTime(path) : default;
            if (path == _loadedPath && writeTime == _loadedWriteTime)
                return;

            _loadedPath = path;
            _loadedWriteTime = writeTime;
            Loaded = null;
            if (path == null)
                return;

            try
            {
                Loaded = PsdDocumentCache.Load(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or NotSupportedException or OverflowException)
            {
                // An unreadable file draws nothing; the layer editors show the reason.
            }
        }

        private static DateTime GetWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return default;
            }
        }

        private Dictionary<PsdLayer, bool> ChooseShapes(PsdDocument document, TimeSpan localTime)
        {
            var choices = new Dictionary<PsdLayer, bool>();

            PsdLayer? closed = Find(document, MouthClosed);
            PsdLayer? half = Find(document, MouthHalfOpen);
            PsdLayer? open = Find(document, MouthOpen);
            if (closed != null || half != null || open != null)
            {
                double level = MeasureLevel(localTime - LipSyncOffset);
                PsdLayer? target = LipSync.GetShape(level, HalfOpenThreshold, OpenThreshold) switch
                {
                    MouthShape.Open => open ?? half ?? closed,
                    MouthShape.HalfOpen => half ?? open ?? closed,
                    _ => closed,
                };
                AddChoice(choices, target, closed, half, open);
            }

            PsdLayer? eyeOpen = Find(document, EyeOpen);
            PsdLayer? eyeHalf = Find(document, EyeHalfOpen);
            PsdLayer? eyeClosed = Find(document, EyeClosed);
            if (eyeOpen != null || eyeHalf != null || eyeClosed != null)
            {
                EyeShape shape = BlinkSchedule.GetShape(
                    localTime,
                    TimeSpan.FromSeconds(BlinkInterval),
                    BlinkRandomness / 100.0,
                    TimeSpan.FromSeconds(BlinkDuration),
                    BlinkSeed);
                PsdLayer? target = shape switch
                {
                    EyeShape.Closed => eyeClosed ?? eyeHalf,
                    EyeShape.HalfOpen => eyeHalf ?? eyeClosed,
                    _ => eyeOpen,
                };
                AddChoice(choices, target, eyeOpen, eyeHalf, eyeClosed);
            }

            return choices;
        }

        private double MeasureLevel(TimeSpan audioTime)
        {
            SoundSource.Resource? audio = LipSyncAudio;
            if (audio == null)
            {
                _envelope = null;
                return double.NegativeInfinity;
            }

            if (_envelope == null || !ReferenceEquals(_envelope.Source, audio))
                _envelope = new LoudnessEnvelope(audio);

            return _envelope.GetLevel(audioTime, s_levelHold);
        }

        private static void AddChoice(Dictionary<PsdLayer, bool> choices, PsdLayer? target, params PsdLayer?[] candidates)
        {
            // Without a layer for the current shape, leave the stored state alone.
            if (target == null)
                return;

            foreach (PsdLayer? candidate in candidates)
            {
                if (candidate != null)
                    choices[candidate] = candidate == target;
            }
        }

        private static PsdLayer? Find(PsdDocument document, string? path)
        {
            return string.IsNullOrEmpty(path) ? null : document.FindByPath(path);
        }
    }
}
