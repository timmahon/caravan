using Caravan.Language.Simple;

namespace Caravan.DataTypes;

/// <summary>Applies data types to messages (Camel's <c>ContractAdvice</c> logic), shared by route contracts and <c>transform</c> with <c>toType</c>.</summary>
internal static class DataTypeTransformation
{
    /// <summary>
    /// Transforms the message to <paramref name="to"/> (from its <see cref="Message.DataType"/>, or <paramref name="from"/> when given) unless
    /// it already has that type, then sets its data type. Without a known source type only .NET conversion is done; .NET to .NET falls back to
    /// the type converters; .NET to another scheme without transformer assumes the message already conforms.
    /// </summary>
    /// <exception cref="CaravanExchangeException">No transformer applies.</exception>
    public static async ValueTask TransformAsync(Message message, DataType? from, DataType to)
    {
        from ??= message.DataType;
        if (to == from)
        {
            return;
        }

        if (!await DoTransformAsync(message, from, to).ConfigureAwait(false))
        {
            throw new CaravanExchangeException($"No Transformer found for [{from}] -> [{to}]", message.Exchange);
        }

        message.DataType = to;
    }

    /// <summary>Validates the message against <paramref name="type"/> with the context's validator for it.</summary>
    /// <exception cref="ValidationException">No validator, or the message is invalid.</exception>
    public static ValueTask ValidateAsync(Message message, DataType type)
    {
        var validator = message.Exchange.Context.Validators.Resolve(type)
            ?? throw new ValidationException($"No Validator found for type [{type}]", message.Exchange);
        return validator.ValidateAsync(message, type);
    }

    private static async ValueTask<bool> DoTransformAsync(Message message, DataType? from, DataType to)
    {
        if (from is null)
        {
            ConvertIfRequired(message, to);
            return true;
        }

        ConvertIfRequired(message, from);
        var registry = message.Exchange.Context.Transformers;
        if (registry.Resolve(from, to) is { } transformer)
        {
            await transformer.TransformAsync(message, from, to).ConfigureAwait(false);
            ConvertIfRequired(message, to);
            return true;
        }

        if (from.IsClrType)
        {
            ConvertIfRequired(message, to);
            return true;
        }

        if (registry.ResolveChain(from, to) is var (first, via, second))
        {
            await first.TransformAsync(message, from, via).ConfigureAwait(false);
            await second.TransformAsync(message, via, to).ConfigureAwait(false);
            ConvertIfRequired(message, to);
            return true;
        }

        return false;
    }

    private static void ConvertIfRequired(Message message, DataType type)
    {
        if (!type.IsClrType || message.Body is null)
        {
            return;
        }

        var clrType = TypeNameResolver.Resolve(type.Name!)
            ?? throw new CaravanExchangeException($"Cannot resolve the .NET type of data type [{type}]", message.Exchange);
        if (!clrType.IsInstanceOfType(message.Body))
        {
            message.Body = message.Exchange.Context.TypeConverter.Convert(clrType, message.Exchange, message.Body);
        }
    }
}
