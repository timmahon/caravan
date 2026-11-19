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

    [Fact]
    public void Error_handling_lambda_shapes_bind_to_a_single_overload()
    {
        var routes = RouteBuilder.Create(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead")
                .MaximumRedeliveries(2)
                .RedeliveryDelay(100)
                .RedeliveryDelay(TimeSpan.FromMilliseconds(100))
                .RetryWhile(_ => true)
                .RetryWhile(_ => throw new InvalidOperationException())
                .OnRedelivery(_ => { })
                .OnRedelivery(_ => throw new InvalidOperationException())
                .OnPrepareFailure(e => e.Message.Body = null)
                .OnExceptionOccurred(_ => { }));
            b.OnException<IOException>(o => o
                .Handled(true)
                .Handled(_ => true)
                .Handled(Pred.Constant(true))
                .Continued(_ => false)
                .OnWhen(_ => true)
                .RetryWhile(_ => false)
                .OnRedelivery(_ => { })
                .MaximumRedeliveries(1)
                .To("mock:error"));
            b.OnException([typeof(IOException), typeof(TimeoutException)], o => o.Continued(true));
            b.OnCompletion(c => c.OnWhen(_ => true).OnWhen(Expr.Body().IsNull()).To("mock:done"));
            b.From("direct:start")
                .OnException<InvalidOperationException>(o => o.Handled(e => e.Message.Body is null))
                .OnCompletion(c => c.OnFailureOnly())
                .DoTry(
                    t => t.ThrowException(new IOException()).ThrowException(_ => new IOException()).ThrowException(_ => throw new IOException()),
                    c => c
                        .DoCatch<IOException>(_ => { })
                        .DoCatch<IOException>(_ => { }, onWhen: _ => true)
                        .DoCatch<IOException>(_ => { }, Pred.Constant(true))
                        .DoCatch([typeof(IOException)], _ => { }, onWhen: e => e.Message.Body is null)
                        .DoFinally(f => f.Rollback()))
                .ThrowException(typeof(IOException), "x")
                .MarkRollbackOnly()
                .MarkRollbackOnlyLast()
                .Rollback("m");
        }).Build(Context).Single();

        Assert.Equal(5, routes.Outputs.Count);
        Assert.Equal(3, routes.OnExceptions.Count);
        Assert.Single(routes.OnCompletions);
    }
}
