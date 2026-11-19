namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>The elements of a YAML DSL file (the items of its top-level list) and their objects: routes, templates, configurations, beans.</summary>
internal static class TopLevelCatalog
{
    /// <summary>A <c>from</c>: the route's endpoint and steps.</summary>
    public static readonly ObjectSpec From = new()
    {
        InlineOption = "uri",
        Options =
        [
            Opt.Str("uri", "The endpoint URI the route consumes from.", required: true),
            Opt.Map("parameters", "Endpoint options appended to the URI as query parameters."),
            Opt.Str("id", "The id of the route's input."),
            Opt.Ignored("description", OptionKind.String, "describe the route instead"),
            Opt.StepsOption,
            Opt.Str("variableReceive", "A variable receiving the consumed message (body; headers as name.header.key); the route starts with an empty message."),
        ],
    };

    /// <summary>An <c>inputType</c>/<c>outputType</c> of a route.</summary>
    public static readonly ObjectSpec DataTypeContract = new()
    {
        InlineOption = "urn",
        Options =
        [
            Opt.Str("urn", "The data type: scheme:name (json:Order, clr:MyApp.Order).", required: true),
            Opt.Bool("validate", "Whether the message is validated with the context's validator for the type."),
        ],
    };

    /// <summary>A <c>route</c>.</summary>
    public static readonly ObjectSpec Route = new()
    {
        Options =
        [
            Opt.Str("id", "The route id (generated when absent)."),
            Opt.Str("description", "A description of the route."),
            Opt.Str("group", "The route group."),
            Opt.Bool("autoStartup", "Whether the route starts with the context (default true)."),
            Opt.Int("startupOrder", "Routes start in ascending order (and stop in reverse)."),
            Opt.Str("routeConfigurationId", "The route configurations used: ids or patterns, comma-separated."),
            Opt.Bool("streamCache", "Overrides the context's stream caching for this route.").Alias("streamCaching"),
            Opt.Enum("shutdownRoute", "Default, or Defer to stop this route after the others.", "Default", "Defer"),
            Opt.Enum("shutdownRunningTask", "What a stopping consumer completes.", "CompleteCurrentTaskOnly", "CompleteAllTasks"),
            Opt.Ref("errorHandlerRef", "An ErrorHandlerDefinition"),
            Opt.Object("errorHandler", "Caravan: the route's error handler.", ModelCatalog.ErrorHandler),
            Opt.Objects(
                "properties",
                "Route properties.",
                new ObjectSpec { Options = [Opt.Str("key", "The key.", required: true), Opt.Str("value", "The value.")] }),
            Opt.Object("from", "The route's endpoint and steps.", From, required: true),
            Opt.Str("nodePrefixId", "A prefix added to the ids of the route's nodes."),
            Opt.Str("precondition", "A Simple predicate (usually on properties): the route is added only when it is true."),
            Opt.Str("routePolicy", "Route policies: comma-separated names of IRoutePolicy beans.").Alias("routePolicyRef"),
            Opt.Object("inputType", "The data type the route expects (transformed and optionally validated on entry).", DataTypeContract),
            Opt.Object("outputType", "The data type the route produces (transformed and optionally validated on exit).", DataTypeContract),
            Opt.Unsupported("logMask", "log masking is not supported"),
            Opt.Unsupported("messageHistory", "message history is a context option"),
            Opt.Unsupported("trace", "tracing is a context option"),
            Opt.Unsupported("delayer", "the delayer is not supported"),
        ],
    };

    /// <summary>A bean (top level, in a template or in a templated route).</summary>
    public static readonly ObjectSpec Bean = new()
    {
        Options =
        [
            Opt.Str("name", "The registry name.", required: true),
            Opt.Str("type", "The type: a .NET type name (assembly-qualified when not loaded yet), optionally prefixed with #class:.", required: true),
            Opt.Map("properties", "Properties set on the bean (nested mappings or dotted keys set nested properties; #bean:name refers to a bean)."),
            Opt.Any("constructors", "Constructor arguments: a list, or a mapping from index (0, 1...) to value."),
            Opt.Str("factoryMethod", "A static method of the type creating the bean (taking the constructor arguments)."),
            Opt.Str("initMethod", "A method called once the properties are set."),
            Opt.Ignored("destroyMethod", OptionKind.String, "beans implementing IDisposable are disposed by their owner"),
            Opt.Unsupported("factoryBean", "use factoryMethod on the type"),
            Opt.Unsupported("builderClass", "use factoryMethod on the type"),
            Opt.Unsupported("builderMethod", "use factoryMethod on the type"),
            Opt.Unsupported("scriptLanguage", "script beans are not supported"),
            Opt.Unsupported("script", "script beans are not supported"),
        ],
    };

    /// <summary>A <c>routeTemplate</c>.</summary>
    public static readonly ObjectSpec RouteTemplate = new()
    {
        Options =
        [
            Opt.Str("id", "The template id.", required: true),
            Opt.Str("description", "A description of the template."),
            Opt.Objects("parameters", "The template parameters ({{name}} in the route).", new ObjectSpec
            {
                Options =
                [
                    Opt.Str("name", "The parameter name.", required: true),
                    Opt.Str("defaultValue", "The default value (makes the parameter optional)."),
                    Opt.Str("description", "A description."),
                    Opt.Bool("required", "Whether a value must be given (default true)."),
                ],
            }),
            Opt.Objects("beans", "Beans created for each route from the template ({{parameters}} are substituted).", Bean),
            Opt.Object("from", "The template's route endpoint and steps.", From),
            Opt.Object("route", "The template's route (with options).", Route),
        ],
    };

    /// <summary>A <c>templatedRoute</c>.</summary>
    public static readonly ObjectSpec TemplatedRoute = new()
    {
        Options =
        [
            Opt.Str("routeTemplateRef", "The template id.", required: true),
            Opt.Str("routeId", "The id of the created route."),
            Opt.Objects("parameters", "The parameter values.", new ObjectSpec
            {
                Options = [Opt.Str("name", "The parameter name.", required: true), Opt.Str("value", "The value.")],
            }),
            Opt.Objects("beans", "Beans local to the created route.", Bean),
            Opt.Str("prefixId", "A prefix added to the ids of the created route's nodes."),
        ],
    };

    /// <summary>A <c>routeConfiguration</c>.</summary>
    public static readonly ObjectSpec RouteConfiguration = new()
    {
        Options =
        [
            Opt.Str("id", "The configuration id (routes select it with routeConfigurationId; without id it applies to routes selecting none)."),
            Opt.Str("description", "A description."),
            Opt.Object("errorHandler", "The error handler.", ModelCatalog.ErrorHandler),
            Opt.Objects("onException", "Exception clauses.", ModelCatalog.OnException) with { ItemWrapper = "onException" },
            Opt.Objects("onCompletion", "Completion blocks.", ModelCatalog.OnCompletion) with { ItemWrapper = "onCompletion" },
            Opt.Objects("intercept", "Interceptors of every node.", ModelCatalog.Intercept) with { ItemWrapper = "intercept" },
            Opt.Objects("interceptFrom", "Interceptors of route inputs.", ModelCatalog.InterceptFrom) with { ItemWrapper = "interceptFrom" },
            Opt.Objects("interceptSendToEndpoint", "Interceptors of sends.", ModelCatalog.InterceptSendToEndpoint) with { ItemWrapper = "interceptSendToEndpoint" },
            Opt.Str("precondition", "A Simple predicate (usually on properties): the configuration is added only when it is true."),
        ],
    };

    /// <summary>The top-level elements (each item of the file's list has exactly one).</summary>
    public static readonly ObjectSpec Root = new()
    {
        Options =
        [
            Opt.Object("route", "A route.", Route),
            Opt.Object("from", "A route without options.", From),
            Opt.Object("routeTemplate", "A route template (Kamelet).", RouteTemplate),
            Opt.Object("templatedRoute", "A route created from a template.", TemplatedRoute),
            Opt.Object("routeConfiguration", "Error handling and interceptors shared by routes.", RouteConfiguration),
            Opt.Objects("beans", "Beans bound in the registry.", Bean),
            Opt.Object("errorHandler", "The error handler of the routes of this file.", ModelCatalog.ErrorHandler),
            Opt.Object("onException", "An exception clause for the routes of this file.", ModelCatalog.OnException),
            Opt.Object("onCompletion", "A completion block for the routes of this file.", ModelCatalog.OnCompletion),
            Opt.Object("intercept", "Intercepts every node of the routes of this file.", ModelCatalog.Intercept),
            Opt.Object("interceptFrom", "Intercepts the inputs of the routes of this file.", ModelCatalog.InterceptFrom),
            Opt.Object("interceptSendToEndpoint", "Intercepts sends to matching endpoints.", ModelCatalog.InterceptSendToEndpoint),
            Opt.Object("rest", "A REST API: services (get, post, ...) each becoming a route.", RestCatalog.Rest),
            Opt.Object("restConfiguration", "The context's REST configuration.", RestCatalog.Configuration),
            Opt.Objects("transformers", "Caravan: data type transformers added to the context.", DataTypeCatalog.Transformer),
            Opt.Objects("validators", "Caravan: data type validators added to the context.", DataTypeCatalog.Validator),
            Opt.Unsupported("camelContext", "configure the context in code or through Caravan.Hosting options"),
            Opt.Unsupported("dataFormats", "declare data formats in code (context.DataFormats) or inline in marshal/unmarshal"),
        ],
    };
}
