using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Bean;

/// <summary>A <c>bean</c> or <c>class</c> endpoint: its producer calls the bean method (see <see cref="BeanProcessor"/>).</summary>
public sealed class BeanEndpoint : EndpointBase
{
    private readonly BeanHolder _holder;

    internal BeanEndpoint(string uri, BeanComponent component, BeanHolder holder, string beanName)
        : base(uri, component, component.Context!)
    {
        _holder = holder;
        BeanName = beanName;
    }

    /// <summary>The registry name or the type name of the bean.</summary>
    public string BeanName { get; }

    /// <summary>The method specification (<c>method</c> option): <c>name</c>, <c>name(string, int)</c> or <c>name(${body}, 'x')</c>.</summary>
    public string? Method { get; set; }

    /// <summary>How long the bean is kept (<c>scope</c> option, default <see cref="BeanScope.Singleton"/>).</summary>
    public BeanScope Scope { get; internal set; }

    /// <inheritdoc />
    public override IProducer CreateProducer() => new BeanProducer(this, new BeanProcessor(_holder, Method));

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The bean component does not support consumers");

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (_holder.Type is not null || Scope == BeanScope.Singleton)
        {
            BeanProcessor.Validate(Context, _holder, Method);
        }

        return Task.CompletedTask;
    }
}
