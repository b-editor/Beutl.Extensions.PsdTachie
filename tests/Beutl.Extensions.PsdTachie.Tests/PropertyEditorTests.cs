using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Extensibility;
using Beutl.Extensions.PsdTachie.Editors;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;

namespace Beutl.Extensions.PsdTachie.Tests;

// Drives the property editors in a headless Avalonia session, the way the element property panel shows them.
public class PropertyEditorTests
{
    private static string TestData(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);

    private static PsdTachieDrawable CreateDrawable()
    {
        var drawable = new PsdTachieDrawable();
        drawable.Source.Value = new FileInfo(TestData("tachie_rle.psd"));
        return drawable;
    }

    private static T Show<T>(T editor)
        where T : Control
    {
        var window = new Window { Width = 400, Height = 800, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    private static List<IPropertyAdapter> Adapters(PsdTachieDrawable drawable) =>
        drawable.Properties.Select(p => (IPropertyAdapter)Activator.CreateInstance(
            typeof(EnginePropertyAdapter<>).MakeGenericType(p.ValueType), p, drawable)!).ToList();

    [Test]
    public void ExtensionClaimsOnlyTheLayerTree()
    {
        List<IPropertyAdapter> adapters = Adapters(CreateDrawable());

        string[] claimed = adapters
            .Where(a => PsdPropertyEditorExtension.GetKind(a) == PsdEditorKind.LayerState)
            .Select(a => a.GetEngineProperty()!.Name)
            .ToArray();

        // The PSD file itself uses Beutl's own FileInfo editor.
        Assert.That(claimed, Is.EqualTo(new[] { nameof(PsdTachieDrawable.Layers) }));
    }

    [Test]
    public void MouthAndEyeLayersAreSavedButGetNoRows()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        string[] shapes =
        [
            nameof(PsdTachieDrawable.MouthClosed), nameof(PsdTachieDrawable.MouthHalfOpen), nameof(PsdTachieDrawable.MouthOpen),
            nameof(PsdTachieDrawable.EyeOpen), nameof(PsdTachieDrawable.EyeHalfOpen), nameof(PsdTachieDrawable.EyeClosed),
        ];

        // The layer tree assigns them, so the property panel lists none of them; they are still properties, so
        // they are saved and undone like the rest.
        Assert.That(drawable.GetDisplayProperties().Select(p => p.Name).Intersect(shapes), Is.Empty);
        Assert.That(drawable.Properties.Select(p => p.Name), Is.SupersetOf(shapes));
    }

    [AvaloniaTest]
    public void ExtensionCreatesTheLayerTree()
    {
        var extension = new PsdPropertyEditorExtension();
        IPropertyAdapter layers = Adapters(CreateDrawable()).Single(a => a.GetEngineProperty()!.Name == nameof(PsdTachieDrawable.Layers));

        object registry = typeof(PropertyEditorExtension).GetProperty("Registry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(extension)!;
        object registration = ((IEnumerable)registry.GetType().GetProperty("Registrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(registry)!).Cast<object>().Single();
        var createContext = (Func<IReadOnlyList<IPropertyAdapter>, IPropertyEditorContext?>)registration.GetType()
            .GetProperty("CreateContext")!.GetValue(registration)!;
        using IPropertyEditorContext? context = createContext([layers]);
        var createControl = (Delegate)registration.GetType().GetProperty("CreateControl")!.GetValue(registration)!;
        object panel = Enum.Parse(createControl.GetType().GenericTypeArguments[1], "Panel");
        var control = (Control?)createControl.DynamicInvoke(context, panel);
        Assert.That(control, Is.TypeOf<PsdLayerTreeEditor>());
        Assert.That(createControl.DynamicInvoke(context, panel), Is.Not.SameAs(control));

        Show(control!);
        foreach (TreeViewItem item in control!.GetVisualDescendants().OfType<TreeViewItem>().ToList())
            item.IsExpanded = true;
        var window = (Window)TopLevel.GetTopLevel(control)!;
        foreach (int width in new[] { 280, 400 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "rendered");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"psd-editor-{width}.png");
            frame!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            TestContext.AddTestAttachment(path);
        }
        window.Close();
    }

    [AvaloniaTest]
    public void LayerTreeTogglesWriteTheStoredState()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        var adapter = new EnginePropertyAdapter<string>(drawable.Layers, drawable);
        var context = new PsdEditorContext<string>(new PsdPropertyEditorExtension(), adapter, PsdEditorKind.LayerState);
        PsdLayerTreeEditor editor = Show(new PsdLayerTreeEditor(context));

        CheckBox hidden = FindToggle<CheckBox>(editor, "非表示");
        Assert.That(hidden.IsChecked, Is.False);
        hidden.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.That(drawable.Layers.Value, Is.EqualTo("+非表示"));

        // A forced '!' layer cannot be switched off.
        Assert.That(FindToggle<CheckBox>(editor, "📁 体").IsEnabled, Is.True);
        Assert.That(FindToggle<CheckBox>(editor, "輪郭").IsEnabled, Is.False);

        // Choosing a radio layer turns its radio siblings off.
        TreeViewItem eyes = editor.GetVisualDescendants().OfType<TreeViewItem>()
            .First(i => i.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "📁 目"));
        eyes.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        RadioButton closed = FindToggle<RadioButton>(eyes, "閉じ");
        closed.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.That(drawable.Layers.Value.Split('\n'), Is.EquivalentTo(new[] { "+非表示", "-目/*開き", "+目/*閉じ" }));
        Assert.That(FindToggle<RadioButton>(eyes, "開き").IsChecked, Is.False);

        // Changing the value from outside (undo, for example) updates the check boxes.
        adapter.SetValue("");
        Dispatcher.UIThread.RunJobs();
        Assert.That(hidden.IsChecked, Is.False);
    }

    [AvaloniaTest]
    public void LayerLabelsSitOnTheirCheckBoxesAndRadioButtons()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        var adapter = new EnginePropertyAdapter<string>(drawable.Layers, drawable);
        var context = new PsdEditorContext<string>(new PsdPropertyEditorExtension(), adapter, PsdEditorKind.LayerState);
        var editor = new PsdLayerTreeEditor(context);
        // Beutl's FluentAvalonia theme tops the radio button label and pads it down 6px to meet the glyph.
        var topped = new Style(x => x.OfType<RadioButton>());
        topped.Setters.Add(new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Top));
        topped.Setters.Add(new Setter(ContentControl.PaddingProperty, new Thickness(8, 6, 8, 0)));
        editor.Styles.Add(topped);
        Show(editor);
        foreach (TreeViewItem item in editor.GetVisualDescendants().OfType<TreeViewItem>().ToList())
            item.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        static double CenterY(Visual visual, Visual root) => visual.TranslatePoint(new Point(0, visual.Bounds.Height / 2), root)!.Value.Y;

        var toggles = editor.GetVisualDescendants().OfType<ToggleButton>().Where(t => t is CheckBox or RadioButton).ToList();
        Assert.That(toggles.OfType<RadioButton>(), Is.Not.Empty);
        foreach (ToggleButton toggle in toggles)
        {
            TextBlock label = toggle.GetVisualDescendants().OfType<TextBlock>().First();
            // The glyph is the radio button's outer ring or the check box's square.
            Control glyph = toggle is RadioButton
                ? toggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().First()
                : toggle.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle");
            Assert.That(CenterY(label, toggle), Is.EqualTo(CenterY(glyph, toggle)).Within(1), label.Text);
        }
    }

    [AvaloniaTest]
    public void TheTreeLinesUpWithThePanelsOtherRows()
    {
        PsdLayerTreeEditor editor = ShowTree(CreateDrawable());

        // Beutl's rows keep 4px outside and 4px around their header and value, so both start 8px in; an editor
        // as wide as the panel ends 8px from the right, under the rows' menu buttons.
        TextBlock header = editor.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "レイヤー");
        TreeView tree = editor.GetVisualDescendants().OfType<TreeView>().Single();
        Assert.That(header.TranslatePoint(default, editor)!.Value.X, Is.EqualTo(8));
        Assert.That(tree.TranslatePoint(default, editor)!.Value.X, Is.EqualTo(8));
        Assert.That(tree.TranslatePoint(new Point(tree.Bounds.Width, 0), editor)!.Value.X, Is.EqualTo(editor.Bounds.Width - 8));
    }

    // The row of a layer inside a group, expanding the group first.
    private static DockPanel LayerRow(Control editor, string group, string layer)
    {
        TreeViewItem item = editor.GetVisualDescendants().OfType<TreeViewItem>()
            .First(i => i.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == group));
        item.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        return item.GetVisualDescendants().OfType<DockPanel>()
            .First(p => p.Children.OfType<ComboBox>().Any() && p.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == layer));
    }

    private static ComboBox ShapeBox(DockPanel row) => row.Children.OfType<ComboBox>().Single();

    private static void Hover(Control target)
    {
        var window = (Window)TopLevel.GetTopLevel(target)!;
        window.MouseMove(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Choose(DockPanel row, string shape)
    {
        ComboBox box = ShapeBox(row);
        box.SelectedIndex = ((IEnumerable<string>)box.ItemsSource!).ToList().IndexOf(shape);
        Dispatcher.UIThread.RunJobs();
    }

    // What the row's combo box shows, or null while it is hidden.
    private static string? Shown(DockPanel row)
    {
        ComboBox box = ShapeBox(row);
        return box.IsVisible ? box.SelectedItem as string ?? box.PlaceholderText : null;
    }

    private static PsdLayerTreeEditor ShowTree(PsdTachieDrawable drawable, IPropertyEditorContextVisitor? visitor = null)
    {
        var adapter = new EnginePropertyAdapter<string>(drawable.Layers, drawable);
        var context = new PsdEditorContext<string>(new PsdPropertyEditorExtension(), adapter, PsdEditorKind.LayerState);
        if (visitor != null)
            context.Accept(visitor);
        return Show(new PsdLayerTreeEditor(context));
    }

    [AvaloniaTest]
    public void TheShapeBoxShowsOnHoverAndAssignsTheLayer()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        PsdLayerTreeEditor editor = ShowTree(drawable);
        DockPanel open = LayerRow(editor, "📁 口", "開き");
        DockPanel half = LayerRow(editor, "📁 口", "半開き");
        Assert.That(Shown(open), Is.Null);

        Hover(open);
        Assert.That(Shown(open), Is.EqualTo("割り当て"));
        Assert.That(ShapeBox(open).ItemsSource, Is.EqualTo(new[] { "（なし）", "閉じた口", "半開きの口", "開いた口", "開いた目", "半目", "閉じた目" }));
        // It sits at the right of the row, after the layer name.
        Assert.That(ShapeBox(open).Bounds.Left, Is.GreaterThan(open.GetVisualDescendants().OfType<ToggleButton>().First().Bounds.Right));

        Choose(open, "開いた口");
        Assert.That(drawable.MouthOpen.Value, Is.EqualTo("口/*開き"));

        // An assigned layer keeps showing its shape when the pointer moves on.
        Hover(half);
        Assert.That(Shown(open), Is.EqualTo("開いた口"));
        Assert.That(Shown(half), Is.EqualTo("割り当て"));

        Hover(open);
        Choose(open, "（なし）");
        Assert.That(drawable.MouthOpen.Value, Is.EqualTo(""));
        Assert.That(Shown(open), Is.EqualTo("割り当て"));
        Hover(half);
        Assert.That(Shown(open), Is.Null);

        // "（なし）" on a layer without a shape changes nothing and leaves the placeholder.
        Choose(half, "（なし）");
        Assert.That(Shown(half), Is.EqualTo("割り当て"));
    }

    [AvaloniaTest]
    public void ShapeBoxesLineUpAtTheRightWhateverTheDepth()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        drawable.MouthOpen.Value = "口/*開き";
        drawable.EyeOpen.Value = "非表示";
        PsdLayerTreeEditor editor = ShowTree(drawable);
        TreeView tree = editor.GetVisualDescendants().OfType<TreeView>().Single();

        double Right(DockPanel row) => ShapeBox(row).TranslatePoint(new Point(ShapeBox(row).Bounds.Width, 0), tree)!.Value.X;

        DockPanel nested = LayerRow(editor, "📁 口", "開き");
        DockPanel top = editor.GetVisualDescendants().OfType<DockPanel>()
            .First(p => p.Children.OfType<ComboBox>().Any() && p.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "非表示"));
        Assert.That(Right(nested), Is.EqualTo(Right(top)));
        Assert.That(Right(top), Is.GreaterThan(tree.Bounds.Width - 40), "At the right of the tree.");
    }

    [AvaloniaTest]
    public void AShapeHasOneLayerAndALayerOneShape()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        PsdLayerTreeEditor editor = ShowTree(drawable);
        DockPanel open = LayerRow(editor, "📁 口", "開き");
        DockPanel half = LayerRow(editor, "📁 口", "半開き");

        Choose(open, "開いた口");
        Choose(half, "開いた口");
        Assert.That(drawable.MouthOpen.Value, Is.EqualTo("口/*半開き"));
        Assert.That(ShapeBox(open).SelectedIndex, Is.EqualTo(-1), "The layer that had the shape loses it.");

        Choose(half, "半開きの口");
        Assert.That(drawable.MouthOpen.Value, Is.EqualTo(""));
        Assert.That(drawable.MouthHalfOpen.Value, Is.EqualTo("口/*半開き"));
    }

    [AvaloniaTest]
    public void ShapeBoxesFollowAssignmentsMadeElsewhere()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        drawable.EyeOpen.Value = "目/*開き";
        PsdLayerTreeEditor editor = ShowTree(drawable);
        DockPanel open = LayerRow(editor, "📁 目", "開き");
        DockPanel closed = LayerRow(editor, "📁 目", "閉じ");
        Assert.That(Shown(open), Is.EqualTo("開いた目"));
        Assert.That(Shown(closed), Is.Null);

        // Undo and redo change the same properties.
        drawable.EyeClosed.Value = "目/*閉じ";
        drawable.EyeOpen.Value = "";
        Dispatcher.UIThread.RunJobs();

        Assert.That(Shown(open), Is.Null);
        Assert.That(Shown(closed), Is.EqualTo("閉じた目"));
    }

    [AvaloniaTest]
    public void MovingALayerToAnotherShapeIsOneUndoableEdit()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        drawable.MouthOpen.Value = "口/*閉じ";
        var scene = new Scene(1920, 1080, "Undo");
        var element = new Element { Length = { Value = MediaTime.FromSeconds(5) } };
        element.AddObject(drawable);
        scene.AddChild(element);
        var sequence = new OperationSequenceGenerator();
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        using var history = new HistoryManager(sequence);
        using IDisposable subscription = history.Subscribe(observer);

        PsdLayerTreeEditor editor = ShowTree(drawable, new HistoryVisitor(history));
        Choose(LayerRow(editor, "📁 口", "閉じ"), "閉じた口");
        Assert.That((drawable.MouthClosed.Value, drawable.MouthOpen.Value), Is.EqualTo(("口/*閉じ", "")));

        Assert.That(history.Undo(), Is.True);
        Assert.That((drawable.MouthClosed.Value, drawable.MouthOpen.Value), Is.EqualTo(("", "口/*閉じ")));

        Assert.That(history.Redo(), Is.True);
        Assert.That((drawable.MouthClosed.Value, drawable.MouthOpen.Value), Is.EqualTo(("口/*閉じ", "")));
    }

    private sealed class HistoryVisitor(HistoryManager history) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType) => serviceType == typeof(HistoryManager) ? history : null;
    }

    private static T FindToggle<T>(Control root, string text)
        where T : Control
    {
        return root.GetVisualDescendants().OfType<T>()
            .First(t => t.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text == text));
    }
}
