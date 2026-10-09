using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Extensions.PsdTachie.Psd;
using Beutl.PropertyAdapters;

namespace Beutl.Extensions.PsdTachie.Editors;

/// <summary>The view-model side of one of this package's property editors.</summary>
internal sealed class PsdEditorContext<T>(PropertyEditorExtension extension, IPropertyAdapter<T> adapter, PsdEditorKind kind)
    : IPropertyEditorContext
{
    private HistoryManager? _history;

    public PropertyEditorExtension Extension { get; } = extension;

    public IPropertyAdapter<T> Adapter { get; } = adapter;

    public PsdEditorKind Kind { get; } = kind;

    /// <summary>The drawable that owns the property, when there is one.</summary>
    public PsdTachieDrawable? Drawable => (Adapter as EnginePropertyAdapter<T>)?.Object as PsdTachieDrawable;

    public void SetAndCommit(T? value, string commandName)
    {
        if (EqualityComparer<T?>.Default.Equals(Adapter.GetValue(), value))
            return;

        Adapter.SetValue(value);
        _history?.Commit(commandName);
    }

    /// <summary>Records the changes made since the last commit, such as to another property, as one undoable edit.</summary>
    public void Commit(string commandName) => _history?.Commit(commandName);

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IServiceProvider services)
        {
            _history = services.GetService(typeof(HistoryManager)) as HistoryManager
                       ?? (services.GetService(typeof(IEditorContext)) as IServiceProvider)?.GetService(typeof(HistoryManager)) as HistoryManager;
        }
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void Dispose()
    {
    }
}

internal enum PsdEditorKind
{
    LayerState,
}

internal static class PsdEditorHelper
{
    /// <summary>Opens the PSD currently assigned to the drawable, or returns why it cannot be opened.</summary>
    public static LoadedPsd? TryLoad(PsdTachieDrawable? drawable, out string? error)
    {
        error = null;
        FileInfo? file = drawable?.Source.CurrentValue;
        if (file == null)
            return null;

        try
        {
            return PsdDocumentCache.Load(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or NotSupportedException or OverflowException)
        {
            error = ex.Message;
            return null;
        }
    }
}
