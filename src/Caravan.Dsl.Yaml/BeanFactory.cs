using System.Reflection;
using Caravan.Support;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// Creates the beans of a <c>beans</c> section: the type (constructor arguments or a static factory method), then the properties (nested
/// mappings and dotted keys set nested properties; values are converted with the type converters, <c>#bean:name</c>/<c>#name</c> refer to
/// registry beans and <c>{{placeholders}}</c> are resolved), then the init method. A section's beans are all created before their properties
/// are set, so properties may refer to beans declared later.
/// </summary>
internal static class BeanFactory
{
    /// <summary>Creates and configures the bean described by <paramref name="bean"/>; <paramref name="substitute"/> rewrites its texts (template parameters).</summary>
    public static object Create(YamlObject bean, Func<string, string>? substitute = null)
    {
        var instance = Instantiate(bean, substitute);
        Configure(bean, instance, substitute);
        return instance;
    }

    /// <summary>Creates the bean (constructor or factory method) without setting its properties.</summary>
    public static object Instantiate(YamlObject bean, Func<string, string>? substitute = null)
    {
        substitute ??= static text => text;
        var context = bean.Context.Context;
        var typeName = substitute(bean.RequiredString("type"));
        var type = TypeResolver.Resolve(context, typeName)
            ?? throw bean.Error("type", $"Cannot resolve the type '{typeName}' of bean '{bean.String("name")}'");
        var arguments = Arguments(bean, substitute);
        try
        {
            return bean.String("factoryMethod") is { } factoryMethod
                ? Invoke(bean, type, factoryMethod, arguments)
                : Construct(bean, type, arguments);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw bean.Error("type", $"Creating bean '{bean.String("name")}' failed: {e.InnerException.Message}", e.InnerException);
        }
    }

    /// <summary>Sets the properties of a bean created by <see cref="Instantiate"/>, then calls its init method.</summary>
    public static void Configure(YamlObject bean, object instance, Func<string, string>? substitute = null)
    {
        substitute ??= static text => text;
        var type = instance.GetType();
        SetProperties(bean, instance, bean.Raw("properties"), string.Empty, substitute);
        if (bean.String("initMethod") is { } initMethod)
        {
            var method = type.GetMethod(initMethod, BindingFlags.Public | BindingFlags.Instance, [])
                ?? throw bean.Error("initMethod", $"{type.Name} has no public method {initMethod}() to call");
            try
            {
                method.Invoke(instance, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw bean.Error("initMethod", $"{type.Name}.{initMethod}() failed: {e.InnerException.Message}", e.InnerException);
            }
        }
    }

    private static List<(YamlNode Node, string? Text)> Arguments(YamlObject bean, Func<string, string> substitute)
    {
        var nodes = bean.Raw("constructors") switch
        {
            null => [],
            YamlSequenceNode sequence => sequence.Children.ToList(),
            YamlMappingNode mapping => mapping.Children
                .Select(e => (Index: int.TryParse(((YamlScalarNode)e.Key).Value, out var i) ? i : throw bean.Context.Error(e.Key, "Constructor argument keys must be indexes (0, 1, ...)"), e.Value))
                .OrderBy(e => e.Index)
                .Select(e => e.Value)
                .ToList(),
            var other => [other],
        };
        return nodes.Select(node => (node, node switch
        {
            YamlScalarNode scalar when YamlObject.IsNull(scalar) => null,
            YamlScalarNode scalar => substitute(scalar.Value ?? string.Empty),
            _ => throw bean.Context.Error(node, "Constructor arguments must be scalars"),
        })).ToList();
    }

    private static object Construct(YamlObject bean, Type type, List<(YamlNode Node, string? Text)> arguments)
    {
        if (arguments.Count == 0 && type.IsValueType)
        {
            return Activator.CreateInstance(type)!;
        }

        Exception? last = null;
        foreach (var constructor in type.GetConstructors().Where(c => Fits(c.GetParameters(), arguments.Count)).OrderBy(c => c.GetParameters().Length))
        {
            if (TryConvert(bean, constructor.GetParameters(), arguments, out var values, ref last))
            {
                return constructor.Invoke(values);
            }
        }

        throw bean.Error(
            arguments.Count == 0 ? "type" : "constructors",
            $"{type.Name} has no public constructor taking {arguments.Count} argument(s) matching the values given" + (last is null ? string.Empty : $": {last.Message}"),
            last);
    }

    private static object Invoke(YamlObject bean, Type type, string name, List<(YamlNode Node, string? Text)> arguments)
    {
        Exception? last = null;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name && Fits(m.GetParameters(), arguments.Count)).OrderBy(m => m.GetParameters().Length))
        {
            if (TryConvert(bean, method.GetParameters(), arguments, out var values, ref last))
            {
                return method.Invoke(null, values) ?? throw bean.Error("factoryMethod", $"{type.Name}.{name} returned null");
            }
        }

        throw bean.Error("factoryMethod", $"{type.Name} has no public static method {name} taking {arguments.Count} argument(s) matching the values given", last);
    }

    private static bool TryConvert(YamlObject bean, ParameterInfo[] parameters, List<(YamlNode Node, string? Text)> arguments, out object?[] values, ref Exception? last)
    {
        values = new object?[parameters.Length];
        for (var i = arguments.Count; i < parameters.Length; i++)
        {
            values[i] = parameters[i].DefaultValue is DBNull ? null : parameters[i].DefaultValue;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            try
            {
                values[i] = Convert(bean.Context.Context, parameters[i].ParameterType, arguments[i].Text);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                last = e;
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a method with <paramref name="parameters"/> can take <paramref name="count"/> arguments (the others being optional).</summary>
    private static bool Fits(ParameterInfo[] parameters, int count) => parameters.Length >= count && parameters.Skip(count).All(p => p.IsOptional);

    private static object? Convert(ICaravanContext context, Type type, string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (text.StartsWith('#'))
        {
            var name = text.StartsWith("#bean:", StringComparison.Ordinal) ? text[6..] : text[1..];
            var bean = context.Registry.Lookup(name) ?? throw new NoSuchBeanException(name);
            return type.IsInstanceOfType(bean) ? bean : throw new InvalidCastException($"Bean {name} is not a {type.Name}");
        }

        text = context.ResolvePropertyPlaceholders(text);
        return type == typeof(Type) ? TypeResolver.Resolve(context, text) : context.TypeConverter.Convert(type, null, text);
    }

    private static void SetProperties(YamlObject bean, object target, YamlNode? node, string path, Func<string, string> substitute)
    {
        if (node is null || YamlObject.IsNull(node))
        {
            return;
        }

        if (node is not YamlMappingNode mapping)
        {
            throw bean.Context.Error(node, $"The properties of bean '{bean.String("name")}' must be a mapping");
        }

        foreach (var (keyNode, value) in mapping.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? string.Empty;
            var (owner, name) = Navigate(bean, target, keyNode, key);
            if (value is YamlMappingNode)
            {
                SetProperties(bean, Child(bean, owner, keyNode, name), value, path + key + ".", substitute);
                continue;
            }

            object? converted = value switch
            {
                YamlScalarNode scalar when YamlObject.IsNull(scalar) => null,
                YamlScalarNode scalar => substitute(scalar.Value ?? string.Empty),
                YamlSequenceNode sequence => sequence.Children.Select(item => item is YamlScalarNode s ? substitute(s.Value ?? string.Empty)
                    : throw bean.Context.Error(item, "List properties must contain scalars")).ToList(),
                _ => throw bean.Context.Error(value, "Unsupported property value"),
            };
            try
            {
                var set = converted is List<string> items
                    ? SetList(bean.Context.Context, owner, name, items)
                    : PropertyBinder.TrySetProperty(bean.Context.Context, owner, name, converted);
                if (!set)
                {
                    throw bean.Context.Error(keyNode, $"Bean '{bean.String("name")}' ({owner.GetType().Name}) has no writable property '{path}{key}'");
                }
            }
            catch (Exception e) when (e is ArgumentException or CaravanException && e is not YamlRouteDefinitionException)
            {
                throw bean.Context.Error(value, $"Cannot set property '{path}{key}' of bean '{bean.String("name")}': {e.Message}", e);
            }
        }
    }

    private static (object Owner, string Name) Navigate(YamlObject bean, object target, YamlNode keyNode, string key)
    {
        var parts = key.Split('.');
        var owner = target;
        foreach (var part in parts[..^1])
        {
            owner = Child(bean, owner, keyNode, part);
        }

        return (owner, parts[^1]);
    }

    private static object Child(YamlObject bean, object owner, YamlNode keyNode, string name)
    {
        var property = owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw bean.Context.Error(keyNode, $"{owner.GetType().Name} has no property '{name}'");
        var value = property.GetValue(owner);
        if (value is null)
        {
            value = Activator.CreateInstance(property.PropertyType)
                ?? throw bean.Context.Error(keyNode, $"Cannot create the value of {owner.GetType().Name}.{property.Name}");
            property.SetValue(owner, value);
        }

        return value;
    }

    private static bool SetList(ICaravanContext context, object owner, string name, List<string> items)
    {
        var property = owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null)
        {
            return false;
        }

        var elementType = property.PropertyType.IsArray
            ? property.PropertyType.GetElementType()!
            : property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments()[0] : typeof(object);
        var values = items.Select(item => Convert(context, elementType, item)).ToList();
        if (property.PropertyType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                array.SetValue(values[i], i);
            }

            property.SetValue(owner, array);
            return true;
        }

        if (property.GetValue(owner) is System.Collections.IList existing && !existing.IsReadOnly)
        {
            existing.Clear();
            values.ForEach(v => existing.Add(v));
            return true;
        }

        if (!property.CanWrite)
        {
            return false;
        }

        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        values.ForEach(v => list.Add(v));
        property.SetValue(owner, list);
        return true;
    }
}
