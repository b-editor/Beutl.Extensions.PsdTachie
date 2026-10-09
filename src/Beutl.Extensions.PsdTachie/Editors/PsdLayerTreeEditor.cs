using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Beutl.Engine;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Extensions.PsdTachie.Psd;

namespace Beutl.Extensions.PsdTachie.Editors;

/// <summary>
/// Shows the PSD's layer tree with a check box (or radio button for '*' layers) per layer. At the right of each row,
/// a combo box assigns the layer to a mouth or eye shape; it shows while the row is hovered, or always once assigned.
/// </summary>
internal sealed class PsdLayerTreeEditor : UserControl
{
    private const string NoShape = "（なし）";

    // Wide enough for the longest shape name, "半開きの口".
    private const double ShapeComboWidth = 112;

    // The mouth and eye shapes a layer can be assigned to, in list order, named as the drawable's properties.
    private static readonly (string Name, Func<PsdTachieDrawable, IProperty<string>> Property)[] s_shapes =
    [
        Shape(nameof(PsdTachieDrawable.MouthClosed), d => d.MouthClosed),
        Shape(nameof(PsdTachieDrawable.MouthHalfOpen), d => d.MouthHalfOpen),
        Shape(nameof(PsdTachieDrawable.MouthOpen), d => d.MouthOpen),
        Shape(nameof(PsdTachieDrawable.EyeOpen), d => d.EyeOpen),
        Shape(nameof(PsdTachieDrawable.EyeHalfOpen), d => d.EyeHalfOpen),
        Shape(nameof(PsdTachieDrawable.EyeClosed), d => d.EyeClosed),
    ];

    private readonly PsdEditorContext<string> _context;
    private readonly TreeView _tree;
    private readonly TextBlock _messageText;
    private readonly ObservableCollection<LayerNode> _roots = [];
    private readonly List<LayerNode> _nodes = [];
    private LoadedPsd? _loaded;
    private bool[]? _flags;
    private bool _updating;
    private IDisposable? _valueSubscription;
    private PsdTachieDrawable? _drawable;

    public PsdLayerTreeEditor(PsdEditorContext<string> context)
    {
        _context = context;
        _tree = new TreeView
        {
            ItemsSource = _roots,
            MaxHeight = 420,
            ItemTemplate = new FuncTreeDataTemplate<LayerNode>(BuildNodeView, node => node.Children),
        };
        _messageText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.7 };

        var resetButton = new Button { Content = "PSDの初期状態に戻す", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        resetButton.Click += (_, _) => _context.SetAndCommit("", "PSDのレイヤーを初期状態に戻す");

        Content = EditorLayout.WithHeader(
            context.Adapter.DisplayName,
            "チェックしたレイヤーを表示します。名前が * で始まるレイヤーは兄弟の中から1つだけ、! で始まるレイヤーは常に表示されます。レイヤーにポインターを合わせると、右側で口パク・まばたきに使う形を選べます。",
            new StackPanel { Children = { _messageText, _tree, resetButton } });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _drawable = _context.Drawable;
        if (_drawable != null)
        {
            _drawable.Source.ValueChanged += OnSourceChanged;
            foreach ((_, Func<PsdTachieDrawable, IProperty<string>> property) in s_shapes)
                property(_drawable).ValueChanged += OnShapeChanged;
        }

        _valueSubscription = _context.Adapter.GetObservable().Subscribe(new ValueObserver(this));
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_drawable != null)
        {
            _drawable.Source.ValueChanged -= OnSourceChanged;
            foreach ((_, Func<PsdTachieDrawable, IProperty<string>> property) in s_shapes)
                property(_drawable).ValueChanged -= OnShapeChanged;
        }

        _drawable = null;
        _valueSubscription?.Dispose();
        _valueSubscription = null;
    }

    private void OnSourceChanged(object? sender, PropertyValueChangedEventArgs<FileInfo?> e)
    {
        Dispatcher.UIThread.Post(Reload);
    }

    // Undo and redo change the assignments too.
    private void OnShapeChanged(object? sender, PropertyValueChangedEventArgs<string> e)
    {
        Dispatcher.UIThread.Post(RefreshShapes);
    }

    private static (string, Func<PsdTachieDrawable, IProperty<string>>) Shape(string property, Func<PsdTachieDrawable, IProperty<string>> get) =>
        (typeof(PsdTachieDrawable).GetProperty(property)?.GetCustomAttribute<DisplayAttribute>()?.Name ?? property, get);

    private void RefreshShapes()
    {
        PsdTachieDrawable? drawable = _context.Drawable;
        foreach (LayerNode node in _nodes)
            node.SetShape(drawable == null ? -1 : Array.FindIndex(s_shapes, s => s.Property(drawable).CurrentValue == node.Layer.Path));
    }

    /// <summary>
    /// Makes the layer the one for <paramref name="shape"/>, or for no shape when it is -1. A layer stands for one
    /// shape at most, so it leaves any other; the layer that had the shape before loses it.
    /// </summary>
    private void OnShapeChosen(LayerNode node, int shape)
    {
        if (_context.Drawable is not { } drawable)
            return;

        bool changed = false;
        for (int i = 0; i < s_shapes.Length; i++)
        {
            IProperty<string> property = s_shapes[i].Property(drawable);
            string value = i == shape ? node.Layer.Path
                : property.CurrentValue == node.Layer.Path ? ""
                : property.CurrentValue;
            if (property.CurrentValue != value)
            {
                property.CurrentValue = value;
                changed = true;
            }
        }

        if (changed)
            _context.Commit("立ち絵のレイヤーを指定");
        RefreshShapes();
    }

    private void Reload()
    {
        _loaded = PsdEditorHelper.TryLoad(_context.Drawable, out string? error);
        _roots.Clear();
        _nodes.Clear();
        if (_loaded == null)
        {
            _flags = null;
            _messageText.Text = error ?? "PSDファイルを選択すると、ここにレイヤーが表示されます。";
            _messageText.IsVisible = true;
            return;
        }

        _messageText.IsVisible = false;
        PsdDocument document = _loaded.Document;
        _flags = PsdLayerState.Resolve(document, _context.Adapter.GetValue());

        // Photoshop lists layers top first; the document stores them bottom first.
        foreach (PsdLayer layer in document.Root.Children.Reverse())
            _roots.Add(CreateNode(layer));
        RefreshShapes();
    }

    private LayerNode CreateNode(PsdLayer layer)
    {
        var node = new LayerNode(this, layer) { IsChecked = _flags![layer.Index] };
        _nodes.Add(node);
        foreach (PsdLayer child in layer.Children.Reverse())
            node.Children.Add(CreateNode(child));
        return node;
    }

    private void RefreshChecks(string? value)
    {
        if (_loaded == null)
            return;

        _flags = PsdLayerState.Resolve(_loaded.Document, value);
        _updating = true;
        try
        {
            foreach (LayerNode node in _nodes)
                node.IsChecked = _flags[node.Layer.Index];
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnNodeToggled(LayerNode node, bool isChecked)
    {
        if (_updating || _loaded == null || _flags == null)
            return;

        PsdLayerState.SetVisible(_flags, node.Layer, isChecked);
        string state = PsdLayerState.Serialize(_loaded.Document, _flags);
        _context.SetAndCommit(state, "PSDのレイヤーを切り替え");
        RefreshChecks(state);
    }

    private Control BuildNodeView(LayerNode node, INameScope scope)
    {
        ToggleButton toggle = node.Layer.IsRadio ? new RadioButton() : new CheckBox();
        toggle.IsEnabled = !node.Layer.IsForced;
        toggle.MinWidth = 0;
        toggle.Padding = new Thickness(4, 0, 0, 0);
        // The themes disagree on where the label goes: FluentAvalonia's radio button puts it at the top behind a
        // 6px padding, which this padding removes. Centred, it sits on the glyph whatever the theme.
        toggle.VerticalContentAlignment = VerticalAlignment.Center;
        toggle[!ToggleButton.IsCheckedProperty] = new Binding(nameof(LayerNode.IsChecked)) { Mode = BindingMode.TwoWay };
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Content = new TextBlock
        {
            Text = node.Layer.IsGroup ? $"📁 {node.Layer.DisplayName}" : node.Layer.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };

        // The shape the layer stands for: lip sync and blinking switch it, whatever its check says.
        var shape = new ComboBox
        {
            ItemsSource = s_shapes.Select(s => s.Name).Prepend(NoShape).ToArray(),
            PlaceholderText = "割り当て",
            Width = ShapeComboWidth,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        shape.Bind(StyledElement.ThemeProperty, shape.GetResourceObservable("LiteComboBoxStyle"));
        ToolTip.SetTip(shape, "口パク・まばたきでこのレイヤーを使う形");

        // Synced by hand rather than bound: after "（なし）" on a layer without a shape the stored -1 has not
        // changed, so a binding would leave "（なし）" showing instead of the placeholder.
        bool syncing = false;
        void ShowStored()
        {
            syncing = true;
            shape.SelectedIndex = node.ShapeIndex;
            syncing = false;
        }

        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerNode.ShapeIndex))
                ShowStored();
        };
        shape.SelectionChanged += (_, _) =>
        {
            if (syncing || shape.SelectedIndex < 0)
                return;

            OnShapeChosen(node, shape.SelectedIndex - 1);
            Dispatcher.UIThread.Post(ShowStored);
        };
        ShowStored();

        var view = new DockPanel { Background = Avalonia.Media.Brushes.Transparent };
        DockPanel.SetDock(shape, Dock.Right);
        view.Children.Add(shape);
        view.Children.Add(toggle);
        shape[!IsVisibleProperty] = new MultiBinding
        {
            Converter = BoolConverters.Or,
            Bindings =
            {
                new Binding(nameof(InputElement.IsPointerOver)) { Source = view },
                new Binding(nameof(ComboBox.IsDropDownOpen)) { Source = shape },
                new Binding(nameof(LayerNode.HasShape)),
            },
        };
        return view;
    }

    private sealed class LayerNode(PsdLayerTreeEditor owner, PsdLayer layer) : INotifyPropertyChanged
    {
        private bool _isChecked;
        private int _shape = -1;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PsdLayer Layer { get; } = layer;

        public ObservableCollection<LayerNode> Children { get; } = [];

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;
                OnPropertyChanged();
                owner.OnNodeToggled(this, value);
            }
        }

        /// <summary>The combo box index of the stored shape: -1, the placeholder, for none; then the shapes after "（なし）".</summary>
        public int ShapeIndex => _shape >= 0 ? _shape + 1 : -1;

        public bool HasShape => _shape >= 0;

        public void SetShape(int shape)
        {
            if (_shape == shape)
                return;

            _shape = shape;
            OnPropertyChanged(nameof(ShapeIndex));
            OnPropertyChanged(nameof(HasShape));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed class ValueObserver(PsdLayerTreeEditor owner) : IObserver<string?>
    {
        public void OnNext(string? value) => Dispatcher.UIThread.Post(() => owner.RefreshChecks(value));

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
