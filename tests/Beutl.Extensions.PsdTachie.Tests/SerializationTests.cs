using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Serialization;

namespace Beutl.Extensions.PsdTachie.Tests;

public class SerializationTests
{
    [Test]
    public void ReadsLegacyTimesAndKeepsLayerChoicesAndThresholdKeyframes()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(0.5) },
            Value = { Value = -30f },
        });
        JsonObject savedAnimation = CoreSerializer.SerializeToJsonObject(animation);
        savedAnimation["KeyFrames"]![0]!["KeyTime"] = "00:00:00.5000000";
        var legacy = new JsonObject
        {
            ["LipSyncOffset"] = "-00:00:00.5000000",
            ["Layers"] = "-体\n+非表示",
            ["MouthOpen"] = "口/*開き",
            ["EyeClosed"] = "目/*閉じ",
            ["BlinkSeed"] = 42,
            ["Animations"] = new JsonObject { ["OpenThreshold"] = savedAnimation },
        };

        var restored = (PsdTachieDrawable)CoreSerializer.DeserializeFromJsonObject(legacy, typeof(PsdTachieDrawable));

        Assert.That(restored.LipSyncOffset.Value, Is.EqualTo(MediaTime.FromSeconds(-0.5)));
        Assert.That(restored.Layers.Value, Is.EqualTo("-体\n+非表示"));
        Assert.That(restored.MouthOpen.Value, Is.EqualTo("口/*開き"));
        Assert.That(restored.EyeClosed.Value, Is.EqualTo("目/*閉じ"));
        Assert.That(restored.BlinkSeed.Value, Is.EqualTo(42));
        KeyFrameAnimation<float>? restoredAnimation = restored.OpenThreshold.GetKeyFrameAnimation();
        Assert.That(restoredAnimation, Is.Not.Null);
        Assert.That(restoredAnimation!.KeyFrames.Single().KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        Assert.That(restoredAnimation.KeyFrames.Single().Value, Is.EqualTo(-30f));

        JsonObject saved = CoreSerializer.SerializeToJsonObject(restored);
        Assert.That(saved["LipSyncOffset"]!.GetValue<long>(), Is.EqualTo(-MediaTime.FlicksPerSecond / 2));
        Assert.That(saved["Animations"]!["OpenThreshold"]!["KeyFrames"]![0]!["KeyTime"]!.GetValue<long>(),
            Is.EqualTo(MediaTime.FlicksPerSecond / 2));
    }

    [Test]
    public void RoundTripKeepsOffsetsBetweenTimeSpanTicks()
    {
        var drawable = new PsdTachieDrawable();
        drawable.LipSyncOffset.Value = new MediaTime(123456789);

        JsonObject saved = CoreSerializer.SerializeToJsonObject(drawable);
        var restored = (PsdTachieDrawable)CoreSerializer.DeserializeFromJsonObject(saved, typeof(PsdTachieDrawable));

        Assert.That(saved["LipSyncOffset"]!.GetValue<long>(), Is.EqualTo(123456789));
        Assert.That(restored.LipSyncOffset.Value, Is.EqualTo(drawable.LipSyncOffset.Value));
    }
}
