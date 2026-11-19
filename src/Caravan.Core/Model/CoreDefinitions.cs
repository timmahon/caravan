using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sends to a static endpoint URI (<c>to</c>).</summary>
public sealed class ToDefinition(string uri, ExchangePattern? pattern = null) : ProcessorDefinition
{
    public string Uri { get; set; } = uri;

    public ExchangePattern? Pattern { get; set; } = pattern;

    public override string ShortName => "to";

    public override string Label => $"to[{Uri}]";

    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new SendProcessor(routeContext.ResolveEndpoint(Uri), Pattern);
}

/// <summary>Runs custom code (<c>process</c>).</summary>
public sealed class ProcessDefinition(IProcessor processor) : ProcessorDefinition
{
    public IProcessor Processor { get; set; } = processor;

    /// <summary>Optional registry name when the processor is resolved by reference.</summary>
    public string? Ref { get; set; }

    public override string ShortName => "process";

    public override string Label => Ref is null ? "process" : $"process[ref:{Ref}]";

    public override IProcessor CreateProcessor(RouteContext routeContext) => Processor;
}

/// <summary>Sets the body (<c>setBody</c>).</summary>
public sealed class SetBodyDefinition(IExpression expression) : ProcessorDefinition
{
    public IExpression Expression { get; set; } = expression;

    public override string ShortName => "setBody";

    public override string Label => $"setBody[{Expression}]";

    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetBodyProcessor(Expression);
}

/// <summary>Transforms the body (<c>transform</c>). Same runtime behaviour as <see cref="SetBodyDefinition"/>.</summary>
public sealed class TransformDefinition(IExpression expression) : ProcessorDefinition
{
    public IExpression Expression { get; set; } = expression;

    public override string ShortName => "transform";

    public override string Label => $"transform[{Expression}]";

    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetBodyProcessor(Expression);
}

/// <summary>Sets a header (<c>setHeader</c>).</summary>
public sealed class SetHeaderDefinition(string name, IExpression expression) : ProcessorDefinition
{
    public string Name { get; set; } = name;

    public IExpression Expression { get; set; } = expression;

    public override string ShortName => "setHeader";

    public override string Label => $"setHeader[{Name}]";

    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetHeaderProcessor(Name, Expression);
}
