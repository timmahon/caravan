using Caravan.Builder;

namespace Caravan.Core.Tests;

/// <summary>Guards against overload ambiguity: these lambda shapes must keep compiling.</summary>
public class DslOverloadTests : ContextTestBase
{
    [Fact]
    public void Common_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .Filter(_ => true, f => f.To("mock:a"))
            .Filter(e => e.Message.Body is null, _ => { })
            .Filter(_ => throw new InvalidOperationException(), _ => { })
            .Filter((string s) => s.Length > 0, f => f.Stop())
            .Filter((Order o) => o.Total > 0, f => f.Log("big"))
            .Choice(c => c
                .When(_ => false, r => r.To("mock:b"))
                .When((int n) => n > 1, r => r.To("mock:c"))
                .When(Expr.Body().IsNull(), _ => { })
                .Otherwise(r => r.ToD(_ => throw new InvalidOperationException())))
            .SetProperty("p", _ => 1)
            .SetProperty("p", _ => throw new InvalidOperationException())
            .SetProperty("p", (string s) => s.Length)
            .SetHeader("h", (Order o) => o.Id)
            .ToD(e => "mock:" + e.ExchangeId)
            .WireTap(_ => "mock:tap", onPrepare: _ => { })
            .WireTap("mock:tap", copy: false)
            .Pipeline(_ => { })
            .Step("s", _ => { })
            .ConvertBodyTo(typeof(string))
            .ConvertHeaderTo("h", typeof(int))
            .RemoveHeaders("*", "keep")
            .RemoveProperties("*"))
            .Build(Context).Single();

        Assert.Equal(19, route.Outputs.Count);
    }
}
