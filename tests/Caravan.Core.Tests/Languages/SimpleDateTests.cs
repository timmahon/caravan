using Caravan.Language.Simple;
using Microsoft.Extensions.Time.Testing;

namespace Caravan.Core.Tests.Languages;

public class SimpleDateTests : SimpleTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 3, 7, 14, 5, 9, 42, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);

    public SimpleDateTests()
    {
        Context.TimeProvider = _clock;
    }

    [Fact]
    public void Now_and_millis_come_from_the_context_clock()
    {
        AssertExpression("${date:now}", Now);
        AssertExpression("${date:millis}", Now.ToUnixTimeMilliseconds());
        _clock.Advance(TimeSpan.FromSeconds(1));
        AssertExpression("${date:millis}", Now.ToUnixTimeMilliseconds() + 1000);
    }

    [Theory]
    [InlineData("yyyy-MM-dd", "2026-03-07")]
    [InlineData("yyyyMMdd", "20260307")]
    [InlineData("yy/M/d", "26/3/7")]
    [InlineData("HH:mm:ss", "14:05:09")]
    [InlineData("HH:mm:ss.SSS", "14:05:09.042")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss.SSSZ", "2026-03-07T14:05:09.042+0000")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ssXXX", "2026-03-07T14:05:09Z")]
    [InlineData("h:mm a", "2:05 PM")]
    [InlineData("K k", "2 14")]
    [InlineData("EEE, d MMM yyyy", "Sat, 7 Mar 2026")]
    [InlineData("EEEE d MMMM", "Saturday 7 March")]
    [InlineData("D u G", "66 6 AD")]
    [InlineData("'at' HH 'o''clock'", "at 14 o'clock")]
    [InlineData("HH:mm:ss.fff", "14:05:09.042")]
    [InlineData("z", "UTC")]
    public void Java_patterns_are_formatted(string pattern, string expected) =>
        AssertExpression("${date:now:" + pattern + "}", expected);

    [Theory]
    [InlineData("now-1h", "2026-03-07 13:05:09")]
    [InlineData("now+1h30m", "2026-03-07 15:35:09")]
    [InlineData("now-2d", "2026-03-05 14:05:09")]
    [InlineData("now+10s", "2026-03-07 14:05:19")]
    [InlineData("now-1h-30m", "2026-03-07 12:35:09")]
    [InlineData("now+1000", "2026-03-07 14:05:10")]
    [InlineData("now+500ms+500ms", "2026-03-07 14:05:10")]
    public void Offsets_shift_the_date(string command, string expected) =>
        AssertExpression("${date:" + command + ":yyyy-MM-dd HH:mm:ss}", expected);

    [Fact]
    public void Header_property_and_exchange_created_dates()
    {
        Exchange.Message.SetHeader("due", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
        Exchange.Message.SetHeader("utc", new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        Exchange.Message.SetHeader("millis", 0L);
        Exchange.Message.SetHeader("text", "2021-12-31T23:59:59Z");
        Exchange.SetProperty("when", new DateTimeOffset(2019, 7, 1, 0, 0, 0, TimeSpan.Zero));

        AssertExpression("${date:header.due:yyyy-MM-dd HH:mm}", "2020-01-02 01:04");
        AssertExpression("${date:header.due+1d:yyyy-MM-dd}", "2020-01-03");
        AssertExpression("${date:in.header.due:yyyy}", "2020");
        AssertExpression("${date:header.utc:HH:mm:ss}", "07:08:09");
        AssertExpression("${date:header.millis:yyyy-MM-dd}", "1970-01-01");
        AssertExpression("${date:header.text:yyyy}", "2021");
        AssertExpression("${date:exchangeProperty.when:yyyy-MM}", "2019-07");
        AssertExpression("${date:header.missing:yyyy}", null);
        AssertExpression("${date:exchangeCreated}", Exchange.Created);
        Assert.Equal(new DateTimeOffset(2019, 7, 1, 0, 0, 0, TimeSpan.Zero), Eval("${date:exchangeProperty.when}"));
    }

    [Fact]
    public void Dates_are_formatted_in_the_clock_local_time_zone_or_a_given_one()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Plus3", TimeSpan.FromHours(3), "Plus3", "Plus3");
        _clock.SetLocalTimeZone(zone);

        AssertExpression("${date:now:HH:mm Z}", "17:05 +0300");
        AssertExpression("${date-with-timezone:now:UTC:HH:mm XXX}", "14:05 Z");
        AssertExpression("${date-with-timezone:now:Asia/Tokyo:yyyy-MM-dd HH:mm XXX}", "2026-03-07 23:05 +09:00");
        AssertExpression("${date-with-timezone:header.missing:UTC:HH}", null);
    }

    [Theory]
    [InlineData("${date:}")]
    [InlineData("${date:file:yyyy}")]
    [InlineData("${date:tomorrow}")]
    [InlineData("${date:now:yyyy-MM-dd qq}")]
    [InlineData("${date:now-1y:yyyy}")]
    [InlineData("${date:now-h:yyyy}")]
    [InlineData("${date-with-timezone:now:yyyy}")]
    [InlineData("${date-with-timezone:now:No/Such_Zone:yyyy}")]
    public void Invalid_date_functions_fail_to_parse(string text)
    {
        var e = Assert.Throws<SimpleParseException>(() => Expr.Simple(text));
        Assert.Equal(2, e.Index);
    }
}
