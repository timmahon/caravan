using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Builds the clauses of a <c>choice</c>: <c>c =&gt; c.When(p, r =&gt; ...).Otherwise(r =&gt; ...)</c>.</summary>
public sealed class ChoiceBuilder
{
    private readonly RouteSteps _steps;

    /// <summary>Creates a builder adding clauses to <paramref name="definition"/>.</summary>
    /// <param name="steps">The block containing the choice (used to create nested blocks).</param>
    /// <param name="definition">The choice being built.</param>
    public ChoiceBuilder(RouteSteps steps, ChoiceDefinition definition)
    {
        _steps = steps;
        Definition = definition;
    }

    /// <summary>The choice being built.</summary>
    public ChoiceDefinition Definition { get; }

    /// <summary>Adds a clause whose <paramref name="block"/> runs when <paramref name="predicate"/> matches and no earlier clause did.</summary>
    public ChoiceBuilder When(IPredicate predicate, Action<RouteSteps> block)
    {
        if (Definition.Otherwise is not null)
        {
            throw new InvalidOperationException("When() cannot follow Otherwise() in a choice");
        }

        var when = new WhenDefinition(predicate) { Parent = Definition, Route = _steps.Route };
        Definition.WhenClauses.Add(when);
        _steps.BuildNested(when.Outputs, when, block);
        return this;
    }

    /// <summary>Adds a clause matching a function of the exchange.</summary>
    public ChoiceBuilder When(Func<Exchange, bool> predicate, Action<RouteSteps> block) => When(Pred.From(predicate), block);

    /// <summary>Adds a clause matching the body converted to <typeparamref name="TBody"/>: <c>.When((string s) =&gt; s.StartsWith("a"), r =&gt; ...)</c>.</summary>
    public ChoiceBuilder When<TBody>(Func<TBody, bool> predicate, Action<RouteSteps> block) => When(Pred.Body(predicate), block);

    /// <summary>Sets the block run when no clause matched. Can be given once, after the <c>When</c> clauses.</summary>
    public ChoiceBuilder Otherwise(Action<RouteSteps> block)
    {
        if (Definition.Otherwise is not null)
        {
            throw new InvalidOperationException("Otherwise() can only be set once in a choice");
        }

        var otherwise = new OtherwiseDefinition { Parent = Definition, Route = _steps.Route };
        Definition.Otherwise = otherwise;
        _steps.BuildNested(otherwise.Outputs, otherwise, block);
        return this;
    }

    /// <summary>Sets the id of the most recently added clause.</summary>
    public ChoiceBuilder Id(string id)
    {
        ProcessorDefinition last = (ProcessorDefinition?)Definition.Otherwise ?? Definition.WhenClauses.LastOrDefault()
            ?? throw new InvalidOperationException("Id() must follow When() or Otherwise()");
        last.Id = id;
        return this;
    }
}
