using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Resolves the data format of <c>marshal</c>/<c>unmarshal</c> definitions.</summary>
internal static class DataFormatDefinitions
{
    public static IDataFormat Resolve(RouteContext routeContext, IDataFormat? dataFormat, string? name)
    {
        var resolved = dataFormat
            ?? (name is null
                ? throw new InvalidOperationException("marshal/unmarshal requires a data format or a data format name")
                : routeContext.Context.ResolveDataFormat(name));
        if (resolved is IService)
        {
            routeContext.AddService(resolved);
        }

        return resolved;
    }
}
