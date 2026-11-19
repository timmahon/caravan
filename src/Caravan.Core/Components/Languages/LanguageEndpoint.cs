using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Languages;

/// <summary>A <c>language:name:script</c> endpoint.</summary>
public sealed class LanguageEndpoint : EndpointBase
{
    private IExpression? _expression;

    internal LanguageEndpoint(string uri, LanguageComponent component, string languageName, string? script)
        : base(uri, component, component.Context!)
    {
        LanguageName = languageName;
        Script = string.IsNullOrEmpty(script) ? null : script;
    }

    /// <summary>The language name (<c>simple</c>, <c>constant</c>, <c>header</c>, ...).</summary>
    public string LanguageName { get; }

    /// <summary>The script; null evaluates the message body as the script.</summary>
    public string? Script { get; }

    /// <summary>Whether the result becomes the message body (default true); otherwise the script only runs for its side effects.</summary>
    public bool Transform { get; set; } = true;

    /// <summary>Whether the script is compiled once (default true); otherwise per message.</summary>
    public bool CacheScript { get; set; } = true;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new LanguageProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException($"Cannot consume from {EndpointUri}: the language component is a producer");

    /// <summary>The expression of the message: the endpoint's script, or the body as the script.</summary>
    internal IExpression ExpressionFor(Exchange exchange)
    {
        if (Script is null)
        {
            var text = exchange.Message.GetBody<string>() ?? throw new InvalidPayloadException(exchange, typeof(string));
            return Context.ResolveLanguage(LanguageName).CreateExpression(text);
        }

        if (CacheScript)
        {
            return _expression ??= Context.ResolveLanguage(LanguageName).CreateExpression(Script);
        }

        return Context.ResolveLanguage(LanguageName).CreateExpression(Script);
    }
}
