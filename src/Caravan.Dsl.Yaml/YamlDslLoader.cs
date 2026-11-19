using Caravan.Builder;
using Caravan.Dsl.Yaml.Catalog;
using Caravan.Model;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// Interprets the top-level elements of YAML DSL documents into a <see cref="YamlRoutesBuilder"/>: beans first (bound in the registry, so
/// that the rest can refer to them), then routes, templates, templated routes, route configurations, the error handler and the
/// builder-level clauses, in file order.
/// </summary>
internal sealed class YamlDslLoader(YamlRoutesBuilder builder, LoadContext context)
{
    /// <summary>Loads the documents.</summary>
    public void Load(IReadOnlyList<YamlDocument> documents)
    {
        var items = new List<(YamlScalarNode Key, string Name, YamlNode Value)>();
        foreach (var document in documents)
        {
            switch (document.RootNode)
            {
                case YamlScalarNode scalar when YamlObject.IsNull(scalar):
                    continue;
                case YamlMappingNode mapping when mapping.Children.Keys.OfType<YamlScalarNode>().Any(k => k.Value is "apiVersion" or "kind"):
                    throw context.Error(mapping, "Kamelet resource files (apiVersion/kind) are not supported; declare a routeTemplate instead");
                case YamlSequenceNode sequence:
                    items.AddRange(sequence.Children.Select(Element));
                    break;
                default:
                    throw context.Error(document.RootNode, "A YAML DSL document must be a list of elements, e.g. - route: { ... }");
            }
        }

        foreach (var (key, _, value) in items.Where(i => i.Name == "beans"))
        {
            var beans = new YamlObject(context, "item", new YamlMappingNode(key, value), TopLevelCatalog.Root).Objects("beans");
            var created = new List<(YamlObject Bean, object Instance)>();
            foreach (var bean in beans)
            {
                var instance = BeanFactory.Instantiate(bean);
                context.Context.Registry.Bind(bean.RequiredString("name"), instance);
                created.Add((bean, instance));
            }

            foreach (var (bean, instance) in created)
            {
                BeanFactory.Configure(bean, instance);
            }
        }

        foreach (var (key, name, value) in items.Where(i => i.Name != "beans"))
        {
            var element = new YamlObject(context, "item", new YamlMappingNode(key, value), TopLevelCatalog.Root);
            LoadElement(element, name);
        }
    }

    private (YamlScalarNode Key, string Name, YamlNode Value) Element(YamlNode item)
    {
        if (item is not YamlMappingNode { Children.Count: 1 } mapping || mapping.Children.First().Key is not YamlScalarNode { Value: { } raw } key)
        {
            throw context.Error(item, "Each element must be an object with exactly one key: route, from, routeTemplate, templatedRoute, routeConfiguration, rest, restConfiguration, transformers, validators, beans, errorHandler, intercept, interceptFrom, interceptSendToEndpoint, onException or onCompletion");
        }

        var name = NameConverter.ToCamelCase(raw);
        if (TopLevelCatalog.Root.Find(name) is { Unsupported: { } why })
        {
            throw context.Error(key, $"'{raw}' is not supported by Caravan: {why}");
        }

        if (TopLevelCatalog.Root.Find(name) is null)
        {
            var known = TopLevelCatalog.Root.Options.Where(o => o.Unsupported is null).Select(o => o.Name).ToList();
            var hint = NameConverter.Closest(name, known.Concat(StepCatalog.Steps.Keys)) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
            var step = StepCatalog.Steps.ContainsKey(name) ? $" ('{raw}' is a step: put it in the steps of a route)" : string.Empty;
            throw context.Error(key, $"Unknown top-level element '{raw}'{step}.{hint} Valid elements: {string.Join(", ", known)}");
        }

        return (key, name, mapping.Children.First().Value);
    }

    private void LoadElement(YamlObject element, string name)
    {
        switch (name)
        {
            case "route":
                LoadRoute(element.Object("route")!, builder.From);
                break;
            case "from":
                LoadFrom(element.Object("from")!, builder.From);
                break;
            case "routeTemplate":
                LoadTemplate(element.Object("routeTemplate")!);
                break;
            case "templatedRoute":
                LoadTemplatedRoute(element.Object("templatedRoute")!);
                break;
            case "routeConfiguration":
                LoadRouteConfiguration(element.Object("routeConfiguration")!);
                break;
            case "rest":
                RestCatalog.FillRest(element.Object("rest")!, builder.Rest(null, _ => { }));
                break;
            case "restConfiguration":
                builder.RestConfiguration(c => RestCatalog.FillConfiguration(element.Object("restConfiguration")!, c.Configuration));
                break;
            case "transformers":
                foreach (var item in element.Objects("transformers"))
                {
                    builder.Transformer(DataTypeCatalog.BuildTransformer(item));
                }

                break;
            case "validators":
                foreach (var item in element.Objects("validators"))
                {
                    builder.Validator(DataTypeCatalog.BuildValidator(item));
                }

                break;
            case "errorHandler":
                builder.ErrorHandler(ModelCatalog.BuildErrorHandler(element.Object("errorHandler")!));
                break;
            case "onException":
                {
                    var options = element.Object("onException")!;
                    var types = options.Types("exception");
                    OnExceptionDefinition definition;
                    try
                    {
                        definition = builder.OnException([.. types]);
                    }
                    catch (ArgumentException e)
                    {
                        throw options.Error("exception", e.Message, e);
                    }

                    ModelCatalog.FillOnException(options, definition);
                    break;
                }

            case "onCompletion":
                ModelCatalog.FillOnCompletion(element.Object("onCompletion")!, builder.OnCompletion(_ => { }));
                break;
            case "intercept":
                ModelCatalog.FillIntercept(element.Object("intercept")!, builder.Intercept(_ => { }));
                break;
            case "interceptFrom":
                {
                    var options = element.Object("interceptFrom")!;
                    ModelCatalog.FillIntercept(options, builder.InterceptFrom(options.String("uri"), _ => { }));
                    break;
                }

            case "interceptSendToEndpoint":
                {
                    var options = element.Object("interceptSendToEndpoint")!;
                    ModelCatalog.FillInterceptSendToEndpoint(options, builder.InterceptSendToEndpoint(options.RequiredString("uri")));
                    break;
                }

            default:
                throw new InvalidOperationException("Unhandled element " + name);
        }
    }

    private static RouteDefinition LoadFrom(YamlObject from, Func<string, RouteDefinitionBuilder> create)
    {
        var route = create(EndpointUris.Build(from)).Definition;
        route.FromId = from.String("id");
        route.FromVariableReceive = from.String("variableReceive");
        from.Steps("steps", route.Outputs, route);
        return route;
    }

    private RouteDefinition LoadRoute(YamlObject options, Func<string, RouteDefinitionBuilder> create)
    {
        var route = LoadFrom(options.Object("from")!, create);
        route.Id = options.String("id") ?? route.Id;
        route.Description = options.String("description");
        route.Group = options.String("group");
        route.AutoStartup = options.Bool("autoStartup") ?? true;
        route.StartupOrder = options.Int("startupOrder");
        route.RouteConfigurationIds = options.String("routeConfigurationId");
        route.StreamCaching = options.Bool("streamCache");
        route.ShutdownRoute = options.Enum<ShutdownRoute>("shutdownRoute") ?? ShutdownRoute.Default;
        route.ShutdownRunningTask = options.Enum<ShutdownRunningTask>("shutdownRunningTask") ?? ShutdownRunningTask.CompleteCurrentTaskOnly;
        route.Precondition = options.String("precondition");
        route.NodePrefixId = options.String("nodePrefixId");
        route.RoutePolicyRefs = options.String("routePolicy");
        route.InputType = Contract(options.Object("inputType"));
        route.OutputType = Contract(options.Object("outputType"));
        if (options.Object("errorHandler") is { } errorHandler)
        {
            route.ErrorHandler = ModelCatalog.BuildErrorHandler(errorHandler);
        }
        else if (options.String("errorHandlerRef") is { } reference)
        {
            route.ErrorHandler = context.Context.Registry.Lookup(reference) as ErrorHandlerDefinition
                ?? throw options.Error("errorHandlerRef", $"No error handler definition (ErrorHandlerDefinition) is bound in the registry as '{reference}'");
        }

        foreach (var property in options.Objects("properties"))
        {
            route.Properties[property.RequiredString("key")] = property.String("value");
        }

        return route;
    }

    private void LoadTemplate(YamlObject options)
    {
        var template = builder.RouteTemplate(options.RequiredString("id"));
        template.Definition.Description = options.String("description");
        foreach (var parameter in options.Objects("parameters"))
        {
            var defaultValue = parameter.String("defaultValue");
            template.TemplateParameter(parameter.RequiredString("name"), defaultValue, parameter.String("description"), parameter.Bool("required") ?? true);
        }

        template.Definition.Beans.AddRange(options.Objects("beans").Select(TemplateBean));
        if (options.Has("from") == options.Has("route"))
        {
            throw context.Error(options.Node, "routeTemplate needs either from or route");
        }

        context.InTemplate = true;
        context.TemplateNeedsRebuild = false;
        var (source, isRoute) = options.Object("route") is { } route ? (route, true) : (options.Object("from")!, false);
        try
        {
            if (isRoute)
            {
                LoadRoute(source, template.From);
            }
            else
            {
                LoadFrom(source, template.From);
            }
        }
        finally
        {
            context.InTemplate = false;
        }

        if (context.TemplateNeedsRebuild)
        {
            // Typed options use parameters: read the route again for each route created, with the parameters substituted.
            template.Configure(rtc =>
            {
                var previous = rtc.Handler;
                rtc.Handler = copy =>
                {
                    Rebuild(copy, source, isRoute, rtc.Parameters);
                    previous?.Invoke(copy);
                };
            });
        }
    }

    private void Rebuild(RouteDefinition copy, YamlObject source, bool isRoute, IDictionary<string, object?> parameters)
    {
        var rebuildContext = new LoadContext(context.Context, context.SourceName) { Substitute = text => TemplateText.Substitute(text, parameters) };
        var loader = new YamlDslLoader(builder, rebuildContext);
        var (id, fromUri) = (copy.Id, copy.FromUri);
        copy.Outputs.Clear();
        copy.OnExceptions.RemoveAll(o => o.RouteScoped);
        copy.OnCompletions.RemoveAll(o => o.RouteScoped);
        if (isRoute)
        {
            loader.LoadRoute(new YamlObject(rebuildContext, "route", source.Node, TopLevelCatalog.Route), _ => new RouteDefinitionBuilder(copy));
        }
        else
        {
            LoadFrom(new YamlObject(rebuildContext, "from", source.Node, TopLevelCatalog.From), _ => new RouteDefinitionBuilder(copy));
        }

        (copy.Id, copy.FromUri) = (id, fromUri);
    }

    private static Caravan.DataTypes.DataTypeContract? Contract(YamlObject? options) =>
        options is null ? null : new(Caravan.DataTypes.DataType.Parse(options.RequiredString("urn")), options.Bool("validate") ?? false);

    private void LoadTemplatedRoute(YamlObject options)
    {
        var templated = builder.TemplatedRoute(options.RequiredString("routeTemplateRef"));
        templated.RouteId = options.String("routeId");
        templated.PrefixId = options.String("prefixId");
        foreach (var parameter in options.Objects("parameters"))
        {
            templated.Parameters[parameter.RequiredString("name")] = parameter.String("value");
        }

        templated.Beans.AddRange(options.Objects("beans").Select(TemplateBean));
    }

    private static TemplateBeanDefinition TemplateBean(YamlObject bean)
    {
        var typeName = bean.RequiredString("type");
        var type = typeName.Contains("{{", StringComparison.Ordinal) ? null : TypeResolver.Resolve(bean.Context.Context, typeName);
        return new TemplateBeanDefinition(
            bean.RequiredString("name"),
            rtc => BeanFactory.Create(bean, text => TemplateText.Substitute(text, rtc.Parameters)),
            type);
    }

    private void LoadRouteConfiguration(YamlObject options)
    {
        var configuration = builder.RouteConfiguration(options.String("id"), _ => { });
        configuration.Description = options.String("description");
        configuration.Precondition = options.String("precondition");
        if (options.Object("errorHandler") is { } errorHandler)
        {
            configuration.ErrorHandler = ModelCatalog.BuildErrorHandler(errorHandler);
        }

        configuration.OnExceptions.AddRange(options.Objects("onException").Select(ModelCatalog.BuildOnException));
        configuration.OnCompletions.AddRange(options.Objects("onCompletion").Select(o => ModelCatalog.FillOnCompletion(o, new OnCompletionDefinition())));
        foreach (var intercept in options.Objects("intercept"))
        {
            var definition = new InterceptDefinition();
            ModelCatalog.FillIntercept(intercept, definition);
            configuration.Intercepts.Add(definition);
        }

        foreach (var intercept in options.Objects("interceptFrom"))
        {
            var definition = new InterceptFromDefinition { Uri = intercept.String("uri") };
            ModelCatalog.FillIntercept(intercept, definition);
            configuration.InterceptFroms.Add(definition);
        }

        foreach (var intercept in options.Objects("interceptSendToEndpoint"))
        {
            var definition = new InterceptSendToEndpointDefinition(intercept.RequiredString("uri"));
            ModelCatalog.FillInterceptSendToEndpoint(intercept, definition);
            configuration.InterceptSendToEndpoints.Add(definition);
        }
    }
}
