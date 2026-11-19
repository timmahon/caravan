using System.Globalization;

namespace Caravan.Language.Simple;

/// <summary>
/// <c>${date:command[:pattern]}</c> and <c>${date-with-timezone:command:timezone:pattern}</c>. Commands: <c>now</c>, <c>millis</c>,
/// <c>exchangeCreated</c>, <c>file</c> (the <c>CaravanFileLastModified</c> header), <c>header.name</c>, <c>exchangeProperty.name</c>, each with optional offsets (<c>now-1h30m</c>,
/// <c>header.due+2d</c>; units <c>d h m s ms</c>, no unit = milliseconds). Without a pattern the value is a <see cref="DateTimeOffset"/>
/// (<c>millis</c>: Unix milliseconds); with one it is formatted (<see cref="JavaDateFormat"/>) in the context's
/// <see cref="TimeProvider.LocalTimeZone"/> or the given time zone.
/// </summary>
internal static class SimpleDateFunction
{
    /// <summary>Creates the node for <paramref name="function"/> (starting with <c>date:</c> or <c>date-with-timezone:</c>).</summary>
    public static SimpleNode Create(string function, int index, string expression)
    {
        var withTimeZone = function.StartsWith("date-with-timezone:", StringComparison.Ordinal);
        var rest = function[(withTimeZone ? "date-with-timezone:".Length : "date:".Length)..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        var command = (colon < 0 ? rest : rest[..colon]).Trim();
        var pattern = colon < 0 ? null : rest[(colon + 1)..];
        TimeZoneInfo? timeZone = null;
        if (withTimeZone)
        {
            var zoneEnd = pattern?.IndexOf(':', StringComparison.Ordinal) ?? -1;
            if (pattern is null || zoneEnd <= 0)
            {
                throw new SimpleParseException($"Valid syntax: ${{date-with-timezone:command:timezone:pattern}} was: {function}", expression, index);
            }

            var zoneId = pattern[..zoneEnd].Trim();
            pattern = pattern[(zoneEnd + 1)..];
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new SimpleParseException($"Unknown time zone '{zoneId}' in: {function}", expression, index, e);
            }
        }

        if (string.IsNullOrEmpty(command))
        {
            throw new SimpleParseException($"Valid syntax: ${{date:command:pattern}} was: {function}", expression, index);
        }

        var (baseCommand, offset) = SplitOffsets(command, function, index, expression);
        var source = CreateSource(baseCommand, function, index, expression);
        JavaDateFormat? format = null;
        if (!string.IsNullOrEmpty(pattern))
        {
            try
            {
                format = JavaDateFormat.Compile(pattern);
            }
            catch (FormatException e)
            {
                throw new SimpleParseException(e.Message, expression, index, e);
            }
        }

        var millis = baseCommand == "millis";
        return new DelegateNode(
            e =>
            {
                if (source(e) is not { } date)
                {
                    return null;
                }

                date += offset;
                if (format is null)
                {
                    return millis ? date.ToUnixTimeMilliseconds() : date;
                }

                return format.Format(TimeZoneInfo.ConvertTime(date, timeZone ?? e.Context.TimeProvider.LocalTimeZone));
            },
            function);
    }

    private static Func<Exchange, DateTimeOffset?> CreateSource(string command, string function, int index, string expression)
    {
        switch (command)
        {
            case "now" or "millis":
                return e => e.Context.TimeProvider.GetUtcNow();
            case "exchangeCreated":
                return e => e.Created;
            case "file":
                return e => ToDate(e, e.Message.GetHeader(CaravanHeaders.FileLastModified), function);
            case "messageTimestamp":
                throw new SimpleParseException($"Command not supported for dateExpression: {command} (no Caravan equivalent)", expression, index);
        }

        foreach (var prefix in (string[])["in.header.", "in.headers.", "header.", "headers."])
        {
            if (command.StartsWith(prefix, StringComparison.Ordinal) && command.Length > prefix.Length)
            {
                var name = command[prefix.Length..];
                return e => ToDate(e, e.Message.GetHeader(name), function);
            }
        }

        if (command.StartsWith("exchangeProperty.", StringComparison.Ordinal) && command.Length > "exchangeProperty.".Length)
        {
            var name = command["exchangeProperty.".Length..];
            return e => ToDate(e, e.GetProperty(name), function);
        }

        throw new SimpleParseException($"Command not supported for dateExpression: {command}", expression, index);
    }

    private static DateTimeOffset? ToDate(Exchange exchange, object? value, string function) => value switch
    {
        null => null,
        DateTimeOffset d => d,
        DateTime { Kind: DateTimeKind.Local } d => new DateTimeOffset(d.ToUniversalTime(), TimeSpan.Zero),
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc), TimeSpan.Zero),
        long or int => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        _ => exchange.Context.TypeConverter.TryConvert(typeof(DateTimeOffset), exchange, value, out var converted) && converted is DateTimeOffset d
            ? d
            : throw new ExpressionEvaluationException(function, $"Cannot use {value.GetType().FullName} '{value}' as a date in ${{{function}}}"),
    };

    private static (string Command, TimeSpan Offset) SplitOffsets(string command, string function, int index, string expression)
    {
        var first = command.IndexOfAny(['+', '-']);
        if (first < 0)
        {
            return (command, TimeSpan.Zero);
        }

        var total = TimeSpan.Zero;
        var i = first;
        while (i < command.Length)
        {
            var sign = command[i] == '-' ? -1 : 1;
            i++;
            var start = i;
            var amount = TimeSpan.Zero;
            while (i < command.Length && command[i] is not ('+' or '-'))
            {
                var digits = i;
                while (i < command.Length && char.IsAsciiDigit(command[i]))
                {
                    i++;
                }

                if (digits == i)
                {
                    throw new SimpleParseException($"Invalid date offset in: {function}", expression, index);
                }

                var number = long.Parse(command[digits..i], CultureInfo.InvariantCulture);
                var unitStart = i;
                while (i < command.Length && char.IsAsciiLetter(command[i]))
                {
                    i++;
                }

                amount += command[unitStart..i] switch
                {
                    "" or "ms" => TimeSpan.FromMilliseconds(number),
                    "s" => TimeSpan.FromSeconds(number),
                    "m" => TimeSpan.FromMinutes(number),
                    "h" => TimeSpan.FromHours(number),
                    "d" => TimeSpan.FromDays(number),
                    var unit => throw new SimpleParseException($"Invalid date offset unit '{unit}' in: {function}", expression, index),
                };
            }

            if (start == i)
            {
                throw new SimpleParseException($"Invalid date offset in: {function}", expression, index);
            }

            total += sign * amount;
        }

        return (command[..first], total);
    }
}
