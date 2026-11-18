using Beutl.Extensibility;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensions.PsdTachie;

[Export]
public sealed class PsdTachieExtension : Extension
{
    public override string Name => "PSD Tachie";

    public override string DisplayName => "PSD立ち絵";

    public override void Load(IExtensionContext context)
    {
        base.Load(context);
        context.Services.GetRequiredService<LibraryService>().RegisterGroup("PSD立ち絵", g => g
            // BindDrawable also binds the EngineObject format, which the timeline requires to accept a drop.
            .AddMultiple("PSD立ち絵", "PSDやZIPの立ち絵パーツを切り替えて表示し、口パクと目パチを行います", m => m.BindDrawable<PsdTachieDrawable>()));
    }
}
