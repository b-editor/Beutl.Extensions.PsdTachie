using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Extensibility;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.PropertyAdapters;

namespace Beutl.Extensions.PsdTachie.Editors;

[Export]
public sealed class PsdPropertyEditorExtension : PropertyEditorExtension
{
    public override string Name => "PSD Tachie property editors";

    public override string DisplayName => "PSD立ち絵のプロパティエディタ";

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        foreach (IPropertyAdapter property in properties)
        {
            if (GetKind(property) != null)
            {
                yield return property;
                yield break;
            }
        }
    }

    public override bool TryCreateContext(IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        context = null;
        if (properties.Count == 0)
            return false;

        IPropertyAdapter property = properties[0];
        switch (GetKind(property))
        {
            case PsdEditorKind.LayerState when property is IPropertyAdapter<string> text:
                context = new PsdEditorContext<string>(this, text, GetKind(property)!.Value);
                return true;
            default:
                return false;
        }
    }

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        control = context switch
        {
            PsdEditorContext<string> { Kind: PsdEditorKind.LayerState } state => new PsdLayerTreeEditor(state),
            _ => null,
        };
        return control != null;
    }

    private static PsdEditorKind? GetKind(IPropertyAdapter property)
    {
        if (property is EnginePropertyAdapter<string> { Object: PsdTachieDrawable })
        {
            Attribute[] attributes = property.GetAttributes();
            if (attributes.OfType<PsdLayerStateAttribute>().Any())
                return PsdEditorKind.LayerState;
        }

        return null;
    }
}
