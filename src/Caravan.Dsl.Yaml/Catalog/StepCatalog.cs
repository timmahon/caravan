namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>
/// The single source of truth of the YAML DSL's steps: their names, options, expression slots and how definitions are built. The loader
/// validates against it and <see cref="YamlDslSchema"/> generates the JSON schema from it.
/// </summary>
internal static class StepCatalog
{
    /// <summary>The steps by camelCase name.</summary>
    public static readonly IReadOnlyDictionary<string, StepSpec> Steps =
        BasicSteps.All.Concat(RoutingSteps.All).Concat(FlowSteps.All).Concat(ErrorSteps.All)
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

    /// <summary>Camel steps Caravan does not support, with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> Unsupported = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["threads"] = "there are no thread pools; use seda or parallelProcessing",
        ["transacted"] = "transactions are not supported",
        ["policy"] = "route policies are not supported",
        ["setVariable"] = "variables are not supported",
        ["setVariables"] = "variables are not supported",
        ["removeVariable"] = "variables are not supported",
        ["convertVariableTo"] = "variables are not supported",
        ["serviceCall"] = "removed from Camel 4",
        ["tokenizer"] = "the LangChain tokenizer is not supported",
        ["poll"] = "use pollEnrich",
        ["onFallback"] = "onFallback belongs inside circuitBreaker",
        ["intercept"] = "intercept goes at the top level of the file or in a routeConfiguration",
        ["interceptFrom"] = "interceptFrom goes at the top level of the file or in a routeConfiguration",
        ["interceptSendToEndpoint"] = "interceptSendToEndpoint goes at the top level of the file or in a routeConfiguration",
        ["whenSkipSendToEndpoint"] = "use interceptSendToEndpoint with skipSendToOriginalEndpoint and onWhen",
    };
}
