using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Builds the clauses of a <c>doTry</c>:
/// <c>c =&gt; c.DoCatch&lt;IOException&gt;(r =&gt; ...).DoCatch([typeof(A), typeof(B)], r =&gt; ..., onWhen: p).DoFinally(r =&gt; ...)</c>.
/// </summary>
public sealed class TryBuilder
{
    private readonly RouteSteps _steps;

    /// <summary>Creates a builder adding clauses to <paramref name="definition"/>.</summary>
    /// <param name="steps">The block containing the doTry (used to create nested blocks).</param>
    /// <param name="definition">The doTry being built.</param>
    public TryBuilder(RouteSteps steps, TryDefinition definition)
    {
        _steps = steps;
        Definition = definition;
    }

    /// <summary>The doTry being built.</summary>
    public TryDefinition Definition { get; }

    /// <summary>Catches <typeparamref name="TException"/> (and subtypes, also as an inner exception), optionally only when <paramref name="onWhen"/> matches.</summary>
    public TryBuilder DoCatch<TException>(Action<RouteSteps> block, IPredicate? onWhen = null)
        where TException : Exception => DoCatch([typeof(TException)], block, onWhen);

    /// <summary>Catches <typeparamref name="TException"/> when <paramref name="onWhen"/> matches.</summary>
    public TryBuilder DoCatch<TException>(Action<RouteSteps> block, Func<Exchange, bool> onWhen)
        where TException : Exception => DoCatch([typeof(TException)], block, Pred.From(onWhen));

    /// <summary>Catches any of <paramref name="exceptionTypes"/>, optionally only when <paramref name="onWhen"/> matches.</summary>
    /// <exception cref="InvalidOperationException">The clause follows <see cref="DoFinally"/>.</exception>
    public TryBuilder DoCatch(Type[] exceptionTypes, Action<RouteSteps> block, IPredicate? onWhen = null)
    {
        if (Definition.Finally is not null)
        {
            throw new InvalidOperationException("DoCatch() cannot follow DoFinally() in a doTry");
        }

        var clause = new CatchDefinition(exceptionTypes) { OnWhen = onWhen, Parent = Definition, Route = _steps.Route };
        Definition.Catches.Add(clause);
        _steps.BuildNested(clause.Outputs, clause, block);
        return this;
    }

    /// <summary>Catches any of <paramref name="exceptionTypes"/> when <paramref name="onWhen"/> matches.</summary>
    public TryBuilder DoCatch(Type[] exceptionTypes, Action<RouteSteps> block, Func<Exchange, bool> onWhen) =>
        DoCatch(exceptionTypes, block, Pred.From(onWhen));

    /// <summary>Sets the block that always runs after the try block and any catch clause. Can be given once, last.</summary>
    /// <exception cref="InvalidOperationException">DoFinally() was already given.</exception>
    public TryBuilder DoFinally(Action<RouteSteps> block)
    {
        if (Definition.Finally is not null)
        {
            throw new InvalidOperationException("DoFinally() can only be set once in a doTry");
        }

        var clause = new FinallyDefinition { Parent = Definition, Route = _steps.Route };
        Definition.Finally = clause;
        _steps.BuildNested(clause.Outputs, clause, block);
        return this;
    }

    /// <summary>Sets the id of the most recently added clause.</summary>
    /// <exception cref="InvalidOperationException">No clause was added yet.</exception>
    public TryBuilder Id(string id)
    {
        ProcessorDefinition last = (ProcessorDefinition?)Definition.Finally ?? Definition.Catches.LastOrDefault()
            ?? throw new InvalidOperationException("Id() must follow DoCatch() or DoFinally()");
        last.Id = id;
        return this;
    }
}
