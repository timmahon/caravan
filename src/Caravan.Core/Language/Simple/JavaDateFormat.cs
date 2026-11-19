using System.Globalization;
using System.Text;

namespace Caravan.Language.Simple;

/// <summary>
/// Formats dates with Java <c>SimpleDateFormat</c> patterns, as used by Camel's <c>${date:now:yyyy-MM-dd'T'HH:mm:ss.SSSZ}</c>
/// (invariant culture). Letters: <c>G y Y M L d D E u a H k K h m s S z Z X</c>, plus <c>f</c> (.NET fraction of a second);
/// <c>'text'</c> is literal, <c>''</c> an apostrophe.
/// </summary>
internal sealed class JavaDateFormat
{
    private readonly List<(char Letter, int Count, string? Literal)> _tokens;

    private JavaDateFormat(List<(char Letter, int Count, string? Literal)> tokens)
    {
        _tokens = tokens;
    }

    /// <summary>Compiles <paramref name="pattern"/>.</summary>
    /// <exception cref="FormatException">The pattern has an unsupported letter or an unterminated quote.</exception>
    public static JavaDateFormat Compile(string pattern)
    {
        var tokens = new List<(char, int, string?)>();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    tokens.Add(('\0', 0, "'"));
                    i += 2;
                    continue;
                }

                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= pattern.Length)
                    {
                        throw new FormatException($"Unterminated quote in date pattern: {pattern}");
                    }

                    if (pattern[i] == '\'')
                    {
                        if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(pattern[i++]);
                }

                tokens.Add(('\0', 0, sb.ToString()));
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                if (!"GyYMLdDEuaHkKhmsSzZXf".Contains(c, StringComparison.Ordinal))
                {
                    throw new FormatException($"Illegal pattern character '{c}' in date pattern: {pattern}");
                }

                var start = i;
                while (i < pattern.Length && pattern[i] == c)
                {
                    i++;
                }

                tokens.Add((c, i - start, null));
                continue;
            }

            tokens.Add(('\0', 0, c.ToString()));
            i++;
        }

        return new JavaDateFormat(tokens);
    }

    /// <summary>Formats <paramref name="value"/> in its own offset.</summary>
    public string Format(DateTimeOffset value)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        foreach (var (letter, count, literal) in _tokens)
        {
            if (literal is not null)
            {
                sb.Append(literal);
                continue;
            }

            switch (letter)
            {
                case 'G':
                    sb.Append("AD");
                    break;
                case 'y' or 'Y':
                    sb.Append(count == 2 ? Pad(value.Year % 100, 2) : Pad(value.Year, count));
                    break;
                case 'M' or 'L':
                    sb.Append(count switch
                    {
                        >= 4 => value.ToString("MMMM", culture),
                        3 => value.ToString("MMM", culture),
                        _ => Pad(value.Month, count),
                    });
                    break;
                case 'd':
                    sb.Append(Pad(value.Day, count));
                    break;
                case 'D':
                    sb.Append(Pad(value.DayOfYear, count));
                    break;
                case 'E':
                    sb.Append(value.ToString(count >= 4 ? "dddd" : "ddd", culture));
                    break;
                case 'u':
                    sb.Append(Pad(value.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)value.DayOfWeek, count));
                    break;
                case 'a':
                    sb.Append(value.Hour < 12 ? "AM" : "PM");
                    break;
                case 'H':
                    sb.Append(Pad(value.Hour, count));
                    break;
                case 'k':
                    sb.Append(Pad(value.Hour == 0 ? 24 : value.Hour, count));
                    break;
                case 'K':
                    sb.Append(Pad(value.Hour % 12, count));
                    break;
                case 'h':
                    sb.Append(Pad(value.Hour % 12 == 0 ? 12 : value.Hour % 12, count));
                    break;
                case 'm':
                    sb.Append(Pad(value.Minute, count));
                    break;
                case 's':
                    sb.Append(Pad(value.Second, count));
                    break;
                case 'S':
                    sb.Append(Pad(value.Millisecond, count));
                    break;
                case 'f':
                    sb.Append((value.Ticks % TimeSpan.TicksPerSecond).ToString("D7", culture)[..Math.Min(count, 7)]);
                    break;
                case 'z':
                    sb.Append(value.Offset == TimeSpan.Zero ? "UTC" : "GMT" + Offset(value.Offset, colon: true));
                    break;
                case 'Z':
                    sb.Append(Offset(value.Offset, colon: false));
                    break;
                case 'X':
                    sb.Append(value.Offset == TimeSpan.Zero
                        ? "Z"
                        : count switch
                        {
                            1 => Offset(value.Offset, colon: false)[..3],
                            2 => Offset(value.Offset, colon: false),
                            _ => Offset(value.Offset, colon: true),
                        });
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Pad(int value, int count) => value.ToString(CultureInfo.InvariantCulture).PadLeft(count, '0');

    private static string Offset(TimeSpan offset, bool colon)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:00}{(colon ? ":" : string.Empty)}{abs.Minutes:00}";
    }
}
