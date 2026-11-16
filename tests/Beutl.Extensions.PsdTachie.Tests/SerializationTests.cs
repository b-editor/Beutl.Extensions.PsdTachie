using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Extensions.PsdTachie.Tests;

public class SerializationTests
{
    [Test]
    public void LegacySourceSurvivesMovingTheProjectAndIsCheckedBeforeExport()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory();
        try
        {
            DirectoryInfo project = root.CreateSubdirectory("project");
            const string name = "立ち絵 #1 %20.psd";
            string sourcePath = Path.Combine(project.CreateSubdirectory("assets").FullName, name);
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "tachie_rle.psd"), sourcePath);
            var elementUri = new Uri(Path.Combine(project.CreateSubdirectory("scene").FullName, "tachie.belm"));
            var legacy = new JsonObject { ["Source"] = sourcePath, ["MouthOpen"] = "口/*開き" };
            var drawable = (PsdTachieDrawable)CoreSerializer.DeserializeFromJsonObject(legacy, typeof(PsdTachieDrawable),
                new CoreSerializerOptions { BaseUri = elementUri });
            Assert.That(drawable.Source.Value!.Uri.LocalPath, Is.EqualTo(sourcePath));

            var element = new Element { Length = { Value = MediaTime.FromSeconds(2) } };
            element.AddObject(drawable);
            CoreSerializer.StoreToUri(element, elementUri);
            var saved = JsonNode.Parse(File.ReadAllText(elementUri.LocalPath))!;
            Assert.That(saved["Objects"]![0]!["Source"]!.GetValue<string>(), Does.StartWith("../assets/"));

            project.MoveTo(Path.Combine(root.FullName, "moved"));
            var movedUri = new Uri(Path.Combine(project.FullName, "scene", "tachie.belm"));
            string movedPath = Path.Combine(project.FullName, "assets", name);
            Element restored = CoreSerializer.RestoreFromUri<Element>(movedUri);
            var restoredDrawable = (PsdTachieDrawable)restored.Objects.Single();
            Assert.That(restoredDrawable.MouthOpen.Value, Is.EqualTo("口/*開き"));
            using var resource = restoredDrawable.ToResource(CompositionContext.Default);
            Assert.That(resource.Loaded?.Path, Is.EqualTo(movedPath));
            Assert.That(resource.Frame, Is.Not.Null);

            IReadOnlyList<string> MissingFiles(Element clip)
            {
                var scene = new Scene(1920, 1080, "Export");
                scene.AddChild(clip);
                return ExportSourceValidator.GetMissingPaths(ExportSourceValidator.CollectRenderableSources(scene, MediaTime.Zero));
            }

            Assert.That(MissingFiles(restored), Is.Empty);
            File.Delete(movedPath);
            Assert.That(MissingFiles(CoreSerializer.RestoreFromUri<Element>(movedUri)), Is.EqualTo(new[] { movedPath }));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestCase(@"C:\素材\立ち絵 #1 %41.psb")]
    [TestCase(@"\\server\share\立ち絵 #1 %41.zip")]
    public void LegacyWindowsPathsKeepTheirLiteralNames(string path)
    {
        var legacy = new JsonObject { ["Source"] = path };
        var drawable = (PsdTachieDrawable)CoreSerializer.DeserializeFromJsonObject(legacy, typeof(PsdTachieDrawable));
        Assert.That(drawable.Source.Value!.Uri.LocalPath, Is.EqualTo(path));
    }

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
