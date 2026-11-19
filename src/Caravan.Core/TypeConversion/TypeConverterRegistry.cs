using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Caravan.TypeConversion;

/// <summary>
/// Registry of type converters. Built-in conversions cover strings, numbers, booleans, enums, dates,
/// <see cref="TimeSpan"/>, <see cref="Guid"/>, byte arrays, streams, and collections. Custom converters
/// registered with <see cref="AddConverter"/> take precedence over the built-ins.
/// </summary>
public sealed class TypeConverterRegistry : ITypeConverter
{
    private readonly ConcurrentDictionary<(Type From, Type To), TypeConverterFunc> _converters = new();
    private readonly List<(Type To, TypeConverterFunc Converter)> _fallbacks = [];

    /// <summary>Creates a registry with the built-in converters.</summary>
    public TypeConverterRegistry()
    {
        RegisterBuiltIns();
    }

    /// <summary>Registers a converter from <typeparamref name="TFrom"/> to <typeparamref name="TTo"/>.</summary>
    public void AddConverter<TFrom, TTo>(Func<TFrom, TTo> converter)
        where TFrom : notnull =>
        _converters[(typeof(TFrom), typeof(TTo))] = (object v, Exchange? _, out object? r) =>
        {
            r = converter((TFrom)v);
            return true;
        };

    /// <summary>Registers a converter from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void AddConverter(Type from, Type to, TypeConverterFunc converter) => _converters[(from, to)] = converter;

    /// <summary>Registers a converter tried for any source type when no direct converter exists.</summary>
    public void AddFallbackConverter(Type to, TypeConverterFunc converter)
    {
        lock (_fallbacks)
        {
            _fallbacks.Add((to, converter));
        }
    }

    /// <inheritdoc />
    public object? Convert(Type toType, Exchange? exchange, object? value)
    {
        if (TryConvert(toType, exchange, value, out var result))
        {
            return result;
        }

        throw new NoTypeConversionAvailableException(value, toType);
    }

    /// <inheritdoc />
    public bool TryConvert(Type toType, Exchange? exchange, object? value, out object? result)
    {
        result = null;
        if (value is null)
        {
            return !toType.IsValueType || Nullable.GetUnderlyingType(toType) is not null;
        }

        var target = Nullable.GetUnderlyingType(toType) ?? toType;
        if (target.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        try
        {
            // Exact registered converter, then converters registered for base types/interfaces of the value.
            for (var t = value.GetType(); t is not null; t = t.BaseType)
            {
                if (_converters.TryGetValue((t, target), out var c) && c(value, exchange, out result))
                {
                    return true;
                }
            }

            foreach (var iface in value.GetType().GetInterfaces())
            {
                if (_converters.TryGetValue((iface, target), out var c) && c(value, exchange, out result))
                {
                    return true;
                }
            }

            lock (_fallbacks)
            {
                foreach (var (to, converter) in _fallbacks)
                {
                    if (target.IsAssignableFrom(to) && converter(value, exchange, out result))
                    {
                        return true;
                    }
                }
            }

            return TryBuiltInFallback(target, exchange, value, out result);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    private void RegisterBuiltIns()
    {
        AddConverter<byte[], string>(b => Encoding.UTF8.GetString(b));
        AddConverter<string, byte[]>(s => Encoding.UTF8.GetBytes(s));
        AddConverter<ReadOnlyMemory<byte>, string>(b => Encoding.UTF8.GetString(b.Span));
        AddConverter<ReadOnlyMemory<byte>, byte[]>(b => b.ToArray());
        AddConverter<Stream, byte[]>(ReadAllBytes);
        AddConverter<Stream, string>(s => Encoding.UTF8.GetString(ReadAllBytes(s)));
        AddConverter<byte[], Stream>(b => new MemoryStream(b, writable: false));
        AddConverter<string, Stream>(s => new MemoryStream(Encoding.UTF8.GetBytes(s), writable: false));
        AddConverter<char[], string>(c => new string(c));
        AddConverter<string, char[]>(s => s.ToCharArray());
        AddConverter<StringBuilder, string>(sb => sb.ToString());
        AddConverter<string, TimeSpan>(ParseTimeSpan);
        AddConverter<int, TimeSpan>(ms => TimeSpan.FromMilliseconds(ms));
        AddConverter<long, TimeSpan>(ms => TimeSpan.FromMilliseconds(ms));
        AddConverter<TimeSpan, long>(ts => (long)ts.TotalMilliseconds);
        AddConverter<string, Guid>(Guid.Parse);
        AddConverter<string, Uri>(s => new Uri(s, UriKind.RelativeOrAbsolute));
        AddConverter<string, DateTimeOffset>(s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture));
        AddConverter<string, DateTime>(s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        AddConverter<string, bool>(ParseBool);
        AddConverter<string, char>(s => s.Length == 1 ? s[0] : throw new FormatException($"'{s}' is not a single character"));
        AddConverter<string, Microsoft.Extensions.Logging.LogLevel>(ParseLogLevel);
        RegisterFileConverters();
        AddConverter<string, Type>(s => Language.Simple.TypeNameResolver.Resolve(s) ?? throw new FormatException($"Unknown type: {s}"));
    }

    private bool TryBuiltInFallback(Type target, Exchange? exchange, object value, out object? result)
    {
        result = null;

        if (target == typeof(string))
        {
            result = value switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            return true;
        }

        if (target == typeof(object))
        {
            result = value;
            return true;
        }

        if (target.IsEnum)
        {
            if (value is string s)
            {
                if (Enum.TryParse(target, s, ignoreCase: true, out var parsed))
                {
                    result = parsed;
                    return true;
                }

                return false;
            }

            if (value.GetType().IsPrimitive)
            {
                result = Enum.ToObject(target, value);
                return true;
            }
        }

        if (value is string text && (target.IsPrimitive || target == typeof(decimal)))
        {
            result = System.Convert.ChangeType(text.Trim(), target, CultureInfo.InvariantCulture);
            return true;
        }

        if (value is IConvertible && (target.IsPrimitive || target == typeof(decimal)))
        {
            result = System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            return true;
        }

        // Single element -> collection, or enumerable -> list/array.
        if (target.IsArray && value is IEnumerable enumerable && value is not string)
        {
            var elementType = target.GetElementType()!;
            var items = enumerable.Cast<object?>()
                .Select(i => Convert(elementType, exchange, i))
                .ToList();
            var array = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                array.SetValue(items[i], i);
            }

            result = array;
            return true;
        }

        if (target.IsGenericType && value is IEnumerable source && value is not string)
        {
            var def = target.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) ||
                def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>))
            {
                var elementType = target.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                foreach (var item in source)
                {
                    list.Add(Convert(elementType, exchange, item));
                }

                result = list;
                return true;
            }
        }

        // Last resort: System.ComponentModel type converters (handles many framework types).
        var componentConverter = TypeDescriptor.GetConverter(target);
        if (componentConverter.CanConvertFrom(value.GetType()))
        {
            result = componentConverter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
            return true;
        }

        return false;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is Spi.IStreamCache cache)
        {
            // A stream cache is read from the start and reset, so that it can be read again.
            cache.Reset();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            cache.Reset();
            return copy.ToArray();
        }

        if (stream is MemoryStream ms && ms.Position == 0)
        {
            return ms.ToArray();
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // Camel's level names (TRACE, DEBUG, INFO, WARN, ERROR, OFF) as well as the LogLevel names.
    private static Microsoft.Extensions.Logging.LogLevel ParseLogLevel(string s) => s.Trim().ToUpperInvariant() switch
    {
        "TRACE" => Microsoft.Extensions.Logging.LogLevel.Trace,
        "DEBUG" => Microsoft.Extensions.Logging.LogLevel.Debug,
        "INFO" or "INFORMATION" => Microsoft.Extensions.Logging.LogLevel.Information,
        "WARN" or "WARNING" => Microsoft.Extensions.Logging.LogLevel.Warning,
        "ERROR" => Microsoft.Extensions.Logging.LogLevel.Error,
        "CRITICAL" or "FATAL" => Microsoft.Extensions.Logging.LogLevel.Critical,
        "OFF" or "NONE" => Microsoft.Extensions.Logging.LogLevel.None,
        _ => Enum.Parse<Microsoft.Extensions.Logging.LogLevel>(s.Trim(), ignoreCase: true),
    };

    // A consumed file (GenericFile) is read only when converted: to a stream (opened), bytes, text (in its charset) or its FileInfo.
    private void RegisterFileConverters()
    {
        var file = typeof(Components.File.IGenericFile);
        AddConverter(file, typeof(Stream), (object v, Exchange? _, out object? r) =>
        {
            r = ((Components.File.IGenericFile)v).OpenRead();
            return true;
        });
        AddConverter(file, typeof(byte[]), (object v, Exchange? _, out object? r) =>
        {
            using var stream = ((Components.File.IGenericFile)v).OpenRead();
            r = ReadAllBytes(stream);
            return true;
        });
        AddConverter(file, typeof(string), (object v, Exchange? e, out object? r) =>
        {
            var genericFile = (Components.File.IGenericFile)v;
            var charset = genericFile.Charset ?? e?.GetProperty<string>(ExchangePropertyKeys.CharsetName);
            using var reader = new StreamReader(genericFile.OpenRead(), charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset), detectEncodingFromByteOrderMarks: true);
            r = reader.ReadToEnd();
            return true;
        });
        AddConverter(file, typeof(FileInfo), (object v, Exchange? _, out object? r) =>
        {
            r = ((Components.File.IGenericFile)v).FileObject as FileInfo;
            return r is not null;
        });
    }

    private static bool ParseBool(string s) => s.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" or "" => false,
        _ => throw new FormatException($"'{s}' is not a boolean"),
    };

    /// <summary>
    /// Parses durations: plain milliseconds (<c>500</c>), Camel-style (<c>5s</c>, <c>1m30s</c>, <c>2h</c>, <c>250ms</c>),
    /// ISO-8601 (<c>PT5S</c>) or <see cref="TimeSpan"/> format (<c>00:00:05</c>).
    /// </summary>
    public static TimeSpan ParseTimeSpan(string text)
    {
        var s = text.Trim();
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            return TimeSpan.FromMilliseconds(ms);
        }

        if (s.StartsWith("PT", StringComparison.OrdinalIgnoreCase) || s.StartsWith('P'))
        {
            return System.Xml.XmlConvert.ToTimeSpan(s.ToUpperInvariant());
        }

        if (s.Contains(':'))
        {
            return TimeSpan.Parse(s, CultureInfo.InvariantCulture);
        }

        var total = TimeSpan.Zero;
        var i = 0;
        var matched = false;
        while (i < s.Length)
        {
            var start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
            {
                i++;
            }

            if (start == i)
            {
                throw new FormatException($"Invalid duration: {text}");
            }

            var number = double.Parse(s[start..i], CultureInfo.InvariantCulture);
            var unitStart = i;
            while (i < s.Length && char.IsLetter(s[i]))
            {
                i++;
            }

            var unit = s[unitStart..i].ToLowerInvariant();
            total += unit switch
            {
                "ms" or "millis" => TimeSpan.FromMilliseconds(number),
                "s" or "sec" or "seconds" => TimeSpan.FromSeconds(number),
                "m" or "min" or "minutes" => TimeSpan.FromMinutes(number),
                "h" or "hours" => TimeSpan.FromHours(number),
                "d" or "days" => TimeSpan.FromDays(number),
                _ => throw new FormatException($"Invalid duration unit '{unit}' in: {text}"),
            };
            matched = true;
        }

        return matched ? total : throw new FormatException($"Invalid duration: {text}");
    }
}
