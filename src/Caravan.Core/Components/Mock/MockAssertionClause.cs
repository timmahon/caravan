namespace Caravan.Components.Mock;

/// <summary>
/// Fluent expectations on one received message (<see cref="MockEndpoint.Message(int)"/>) or on every received message
/// (<see cref="MockEndpoint.AllMessages()"/>), Camel's <c>AssertionClause</c>:
/// <code>
/// mock.Message(0).Header("priority").IsEqualTo("high").Body&lt;int&gt;().IsGreaterThan(10);
/// mock.AllMessages().Property("tenant").IsNotNull();
/// </code>
/// Each condition is checked when the mock is asserted.
/// </summary>
public sealed class MockAssertionClause
{
    internal MockAssertionClause(MockEndpoint endpoint, int? index)
    {
        Endpoint = endpoint;
        Index = index;
    }

    /// <summary>The mock endpoint.</summary>
    public MockEndpoint Endpoint { get; }

    /// <summary>The 0-based index of the message, or null for every message.</summary>
    public int? Index { get; }

    /// <summary>Expectations on the message body.</summary>
    /// <returns>The value clause.</returns>
    public MockValueClause Body() => new(this, "body", e => e.Message.Body);

    /// <summary>Expectations on the message body converted to <typeparamref name="T"/> (with the context's type converters).</summary>
    /// <typeparam name="T">The type to convert the body to.</typeparam>
    /// <returns>The value clause.</returns>
    public MockValueClause Body<T>() => new(this, $"body as {typeof(T).Name}", e => e.Message.GetBody<T>());

    /// <summary>Expectations on a header.</summary>
    /// <param name="name">The header name.</param>
    /// <returns>The value clause.</returns>
    public MockValueClause Header(string name) => new(this, $"header {name}", e => e.Message.GetHeader(name));

    /// <summary>Expectations on an exchange property.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value clause.</returns>
    public MockValueClause Property(string name) => new(this, $"property {name}", e => e.GetProperty(name));

    /// <summary>Expectations on any value computed from the exchange.</summary>
    /// <param name="expression">Computes the value.</param>
    /// <returns>The value clause.</returns>
    public MockValueClause Value(IExpression expression) => new(this, expression.ToString() ?? "expression", expression.Evaluate);

    /// <summary>Expects the message(s) to match <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <param name="description">Describes the condition in failure messages.</param>
    /// <returns>This clause.</returns>
    public MockAssertionClause Matches(Func<Exchange, bool> predicate, string description = "predicate") =>
        AddCheck(e => predicate(e) ? null : $"did not match {description}");

    /// <summary>Expects the message(s) to match <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This clause.</returns>
    public MockAssertionClause Matches(IPredicate predicate) => Matches(predicate.Matches, predicate.ToString() ?? "predicate");

    /// <summary>Registers a check on the selected message(s): returns null when it holds, else what is wrong.</summary>
    internal MockAssertionClause AddCheck(Func<Exchange, string?> check)
    {
        Endpoint.Expect(m =>
        {
            var received = m.ReceivedExchanges;
            if (Index is { } index)
            {
                if (index >= received.Count)
                {
                    return $"Message {index} was not received (received {received.Count})";
                }

                return check(received[index]) is { } error ? $"Message {index}: {error}" : null;
            }

            for (var i = 0; i < received.Count; i++)
            {
                if (check(received[i]) is { } error)
                {
                    return $"Message {i}: {error}";
                }
            }

            return null;
        });
        return this;
    }
}
