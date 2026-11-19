using System.Text;
using Caravan.Support;

namespace Caravan;

/// <summary>Factory methods for common expressions (Camel's <c>ExpressionBuilder</c>/<c>Builder</c>).</summary>
public static class Expr
{
    /// <summary>The message body (<c>${body}</c>).</summary>
    public static IExpression Body() => new FuncExpression(e => e.Message.Body, "${body}");

    /// <summary>The message body converted to <typeparamref name="T"/>.</summary>
    public static IExpression Body<T>() => new FuncExpression(e => e.Message.GetBody<T>(), $"${{bodyAs({typeof(T).Name})}}");

    /// <summary>The header <paramref name="name"/> (<c>${header.name}</c>).</summary>
    public static IExpression Header(string name) => new FuncExpression(e => e.Message.GetHeader(name), $"${{header.{name}}}");

    /// <summary>The exchange property <paramref name="name"/> (<c>${exchangeProperty.name}</c>).</summary>
    public static IExpression Property(string name) => new FuncExpression(e => e.GetProperty(name), $"${{exchangeProperty.{name}}}");

    /// <summary>A constant value.</summary>
    public static IExpression Constant(object? value) => new FuncExpression(_ => value, value?.ToString() ?? "null");

    /// <summary>The exchange id.</summary>
    public static IExpression ExchangeId() => new FuncExpression(e => e.ExchangeId, "${exchangeId}");

    /// <summary>The message id.</summary>
    public static IExpression MessageId() => new FuncExpression(e => e.Message.MessageId, "${messageId}");

    /// <summary>The exception on the exchange, if any.</summary>
    public static IExpression Exception() => new FuncExpression(e => e.Exception, "${exception}");

    /// <summary>An expression computed by <paramref name="func"/>; <paramref name="text"/> is used by <c>ToString</c>.</summary>
    public static IExpression From(Func<Exchange, object?> func, string? text = null) => new FuncExpression(func, text);

    /// <summary>A predicate computed by <paramref name="func"/>; <paramref name="text"/> is used by <c>ToString</c>.</summary>
    public static IPredicate Predicate(Func<Exchange, bool> func, string? text = null) => new FuncPredicate(func, text);

    /// <summary>The result of <paramref name="func"/> applied to the body converted to <typeparamref name="T"/>.</summary>
    public static IExpression Body<T>(Func<T, object?> func) =>
        new FuncExpression(e => func(e.Message.Body is null ? default! : e.Message.GetBody<T>()!), $"${{body({typeof(T).Name})}}");

    /// <summary>The header <paramref name="name"/> converted to <typeparamref name="T"/> (null when missing).</summary>
    public static IExpression HeaderAs<T>(string name) =>
        new FuncExpression(e => e.Message.GetHeader(name, typeof(T)), $"${{headerAs({name},{typeof(T).Name})}}");

    /// <summary>The exchange property <paramref name="name"/> converted to <typeparamref name="T"/> (null when missing).</summary>
    public static IExpression PropertyAs<T>(string name) =>
        new FuncExpression(e => e.GetProperty(name) is { } v ? e.Context.TypeConverter.Convert(typeof(T), e, v) : null, $"${{exchangePropertyAs({name},{typeof(T).Name})}}");

    /// <summary>All message headers (<c>${headers}</c>).</summary>
    public static IExpression Headers() => new FuncExpression(e => e.Message.Headers, "${headers}");

    /// <summary>All exchange properties (<c>${exchangeProperties}</c>).</summary>
    public static IExpression Properties() => new FuncExpression(e => e.Properties, "${exchangeProperties}");

    /// <summary>The <see cref="Type"/> of the body, or null when the body is null (<c>${bodyType}</c>).</summary>
    public static IExpression BodyType() => new FuncExpression(e => e.Message.Body?.GetType(), "${bodyType}");

    /// <summary>
    /// The message of the exception on the exchange, or of the caught exception
    /// (<see cref="ExchangePropertyKeys.ExceptionCaught"/>) when the exchange is no longer failed (<c>${exception.message}</c>).
    /// </summary>
    public static IExpression ExceptionMessage() =>
        new FuncExpression(e => (e.Exception ?? e.GetProperty(ExchangePropertyKeys.ExceptionCaught) as Exception)?.Message, "${exception.message}");

    /// <summary>The current time from the context's <see cref="TimeProvider"/>, as a <see cref="DateTimeOffset"/> (<c>${date:now}</c>).</summary>
    public static IExpression Now() => new FuncExpression(e => e.Context.TimeProvider.GetUtcNow(), "${date:now}");

    /// <summary>The current time from the context's <see cref="TimeProvider"/>, in Unix milliseconds.</summary>
    public static IExpression SystemTimeMillis() =>
        new FuncExpression(e => e.Context.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(), "${date:millis}");

    /// <summary>The id of the route currently processing the exchange (<c>${routeId}</c>).</summary>
    public static IExpression RouteId() => new FuncExpression(e => e.CurrentRouteId ?? e.FromRouteId, "${routeId}");

    /// <summary>Concatenates the values of <paramref name="expressions"/> as strings; null values are skipped.</summary>
    public static IExpression Concat(params IExpression[] expressions) =>
        new FuncExpression(
            e =>
            {
                var sb = new StringBuilder();
                foreach (var expression in expressions)
                {
                    sb.Append(ObjectHelper.ToText(e, expression.Evaluate(e)));
                }

                return sb.ToString();
            },
            string.Concat(expressions.Select(x => x.ToString())));

    /// <summary>Appends <paramref name="suffix"/> (a value or an <see cref="IExpression"/>) to the string value of <paramref name="expression"/>.</summary>
    public static IExpression Append(IExpression expression, object? suffix) => Concat(expression, Of(suffix));

    /// <summary>Prepends <paramref name="prefix"/> (a value or an <see cref="IExpression"/>) to the string value of <paramref name="expression"/>.</summary>
    public static IExpression Prepend(IExpression expression, object? prefix) => Concat(Of(prefix), expression);

    /// <summary>
    /// The value of <paramref name="expression"/> converted to <typeparamref name="T"/> with the context's type converters.
    /// Evaluating throws <see cref="TypeConversion.NoTypeConversionAvailableException"/> when the value cannot be converted.
    /// </summary>
    public static IExpression Convert<T>(IExpression expression) =>
        new FuncExpression(e => e.Context.TypeConverter.Convert(typeof(T), e, expression.Evaluate(e)), $"convertTo({expression},{typeof(T).Name})");

    /// <summary>
    /// Replaces every match of <paramref name="regex"/> in the string value of <paramref name="expression"/> with
    /// <paramref name="replacement"/> (a value or an <see cref="IExpression"/>; <c>$1</c> group references are supported).
    /// Null stays null.
    /// </summary>
    public static IExpression RegexReplaceAll(IExpression expression, string regex, object? replacement)
    {
        var compiled = new System.Text.RegularExpressions.Regex(regex, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var replacementExpression = Of(replacement);
        return new FuncExpression(
            e => ObjectHelper.ToText(e, expression.Evaluate(e)) is { } text
                ? compiled.Replace(text, ObjectHelper.ToText(e, replacementExpression.Evaluate(e)) ?? string.Empty)
                : null,
            $"regexReplaceAll({expression},{regex},{replacementExpression})");
    }

    /// <summary>
    /// Tokenizes the body (Camel's <c>tokenize</c>): the parts of the text between occurrences of <paramref name="token"/>, read
    /// lazily. The body may be a string, <see cref="TextReader"/>, <see cref="Stream"/>, byte array or anything convertible to a
    /// string. Use it to split text: <c>.Split(Expr.Tokenize(","), ...)</c>. A trailing token does not produce an empty last part.
    /// </summary>
    /// <param name="token">The separator.</param>
    /// <param name="regex">Whether <paramref name="token"/> is a regular expression.</param>
    /// <param name="group">When above 1, joins that many parts (with the separators between them) into one value, e.g. to split a file in chunks of N lines.</param>
    /// <param name="skipFirst">Skips the first part (e.g. a header line).</param>
    public static IExpression Tokenize(string token, bool regex = false, int group = 0, bool skipFirst = false) =>
        Tokenize(Body(), token, regex, group, skipFirst);

    /// <summary>Tokenizes the value of <paramref name="source"/>; see <see cref="Tokenize(string, bool, int, bool)"/>.</summary>
    /// <param name="source">The text to tokenize.</param>
    /// <param name="token">The separator.</param>
    /// <param name="regex">Whether <paramref name="token"/> is a regular expression.</param>
    /// <param name="group">When above 1, joins that many parts into one value.</param>
    /// <param name="skipFirst">Skips the first part.</param>
    public static IExpression Tokenize(IExpression source, string token, bool regex = false, int group = 0, bool skipFirst = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var separator = new System.Text.RegularExpressions.Regex(
            regex ? token : System.Text.RegularExpressions.Regex.Escape(token),
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return new FuncExpression(e => TokenIterator.Create(e, source.Evaluate(e), separator, group, skipFirst), $"tokenize({source},{token})");
    }

    /// <summary>
    /// The lines of the body (separated by <c>\n</c>, <c>\r\n</c> or <c>\r</c>), read lazily: use it to split large text or streams
    /// line by line, e.g. <c>.Split(Expr.Lines(), ..., streaming: true)</c>. See <see cref="Tokenize(string, bool, int, bool)"/>.
    /// </summary>
    /// <param name="group">When above 1, joins that many lines into one value.</param>
    /// <param name="skipFirst">Skips the first line (e.g. a header).</param>
    public static IExpression Lines(int group = 0, bool skipFirst = false) => Lines(Body(), group, skipFirst);

    /// <summary>The lines of the value of <paramref name="source"/>; see <see cref="Lines(int, bool)"/>.</summary>
    /// <param name="source">The text to split in lines.</param>
    /// <param name="group">When above 1, joins that many lines into one value.</param>
    /// <param name="skipFirst">Skips the first line.</param>
    public static IExpression Lines(IExpression source, int group = 0, bool skipFirst = false) =>
        Tokenize(source, "\\r\\n|\\n|\\r", regex: true, group, skipFirst);

    /// <summary>
    /// A Simple language expression (Camel's <c>simple("...")</c>): text with <c>${...}</c> functions such as
    /// <c>Expr.Simple("Hello ${header.name}, you ordered ${body.Lines[0].Sku}")</c>. A text made of one function evaluates to the
    /// function's value (not a string). Compiled once and cached; see <see cref="Language.SimpleLanguage"/>.
    /// </summary>
    /// <exception cref="Language.Simple.SimpleParseException">The text is invalid.</exception>
    public static IExpression Simple(string text) => Language.SimpleLanguage.Expression(text);

    /// <summary>A Simple expression whose value is converted to <paramref name="resultType"/> (Camel's <c>simple(text, type)</c>).</summary>
    /// <exception cref="Language.Simple.SimpleParseException">The text is invalid.</exception>
    public static IExpression Simple(string text, Type resultType) => Language.SimpleLanguage.Expression(text, resultType);

    /// <summary>A Simple expression whose value is converted to <typeparamref name="T"/>.</summary>
    /// <exception cref="Language.Simple.SimpleParseException">The text is invalid.</exception>
    public static IExpression Simple<T>(string text) => Language.SimpleLanguage.Expression(text, typeof(T));

    /// <summary>Returns <paramref name="value"/> itself when it is an <see cref="IExpression"/>, otherwise a constant expression for it.</summary>
    public static IExpression Of(object? value) => value as IExpression ?? Constant(value);
}
