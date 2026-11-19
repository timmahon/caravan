namespace Caravan.Core.Tests.Languages;

/// <summary>An exchange with a body, headers and properties to evaluate Simple texts against.</summary>
public abstract class SimpleTestBase : ContextTestBase
{
    protected SimpleTestBase()
    {
        Exchange = new Exchange(Context);
        Exchange.Message.Body = "<hello id='m123'>world!</hello>";
        Exchange.Message.SetHeader("foo", "abc");
        Exchange.Message.SetHeader("bar", 123);
        Exchange.Message.SetHeader("dotted.name", "dotted");
        Exchange.SetProperty("prop", "propValue");
        Exchange.FromRouteId = "fromRoute";
        Exchange.CurrentRouteId = "currentRoute";
    }

    protected Exchange Exchange { get; set; }

    protected object? Eval(string text) => Expr.Simple(text).Evaluate(Exchange);

    protected bool Matches(string text) => Pred.Simple(text).Matches(Exchange);

    protected void AssertExpression(string text, object? expected) => Assert.Equal(expected, Eval(text));

    protected void AssertPredicate(string text, bool expected) =>
        Assert.True(Matches(text) == expected, $"Expected '{text}' to be {expected}");
}
