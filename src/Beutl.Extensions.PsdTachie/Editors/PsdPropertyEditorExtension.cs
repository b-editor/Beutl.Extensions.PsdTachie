using Beutl.Extensibility;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.PropertyAdapters;

namespace Beutl.Extensions.PsdTachie.Editors;

[Export]
public sealed class PsdPropertyEditorExtension : PropertyEditorExtension
{
    public override string Name => "PSD Tachie property editors";

    public override string DisplayName => "PSD立ち絵のプロパティエディタ";

    protected override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<string, PsdEditorContext<string>>(
            property => new PsdEditorContext<string>(this, property, PsdEditorKind.LayerState),
            context => new PsdLayerTreeEditor(context),
            new PropertyEditorOptions
            {
                Match = property => GetKind(property) == PsdEditorKind.LayerState,
            });
    }

    internal static PsdEditorKind? GetKind(IPropertyAdapter property)
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
