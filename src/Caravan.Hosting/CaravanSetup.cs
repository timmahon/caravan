using Caravan.Builder;
using Caravan.Spi;

namespace Caravan.Hosting;

/// <summary>What <see cref="ICaravanBuilder"/> collected; read when the context is created.</summary>
internal sealed class CaravanSetup
{
    public List<Func<IServiceProvider, IRoutesBuilder>> Routes { get; } = [];

    public List<(string Scheme, Func<IServiceProvider, IComponent> Factory)> Components { get; } = [];

    public List<Func<IServiceProvider, IRouteTemplateParameterSource>> ParameterSources { get; } = [];

    public List<Action<IServiceProvider, ICaravanContext>> Configurers { get; } = [];
}
