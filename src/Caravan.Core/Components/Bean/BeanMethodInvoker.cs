using System.Linq.Expressions;
using System.Reflection;
using Caravan.Spi;
using Caravan.TypeConversion;

namespace Caravan.Components.Bean;

/// <summary>
/// Calls one bean method on an exchange: binds each parameter (from the method specification's values, binding attributes, the
/// automatically bound types, or the body for the first other parameter), invokes a compiled delegate and awaits <see cref="Task"/>,
/// <see cref="Task{T}"/>, <see cref="ValueTask"/> and <see cref="ValueTask{T}"/> results. Thread-safe and cached per method and specification.
/// </summary>
internal sealed class BeanMethodInvoker
{
    private readonly Func<Exchange, object?>[] _binders;
    private readonly Func<object?, object?[], object?> _invoke;
    private readonly ResultKind _resultKind;
    private readonly Func<object, Task>? _toTask;
    private readonly Func<Task, object?>? _taskResult;

    public BeanMethodInvoker(MethodInfo method, IReadOnlyList<BeanParameterToken>? values)
    {
        Method = method;
        var parameters = method.GetParameters();
        _binders = new Func<Exchange, object?>[parameters.Length];
        var bodyBound = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var token = values is not null && i < values.Count && !values[i].IsTypeToken ? values[i] : null;
            _binders[i] = CreateBinder(method, parameters[i], token, ref bodyBound);
        }

        _invoke = Compile(method);
        (_resultKind, _toTask, _taskResult) = AnalyzeReturnType(method.ReturnType);
    }

    private enum ResultKind
    {
        Void,
        Value,
        Task,
        TaskOfT,
    }

    public MethodInfo Method { get; }

    /// <summary>Whether the method returns a value (directly or through a task) that becomes the body.</summary>
    public bool HasResult => _resultKind is ResultKind.Value or ResultKind.TaskOfT;

    /// <summary>Whether the parameter is bound by an attribute or automatically by its type (it does not take the body).</summary>
    public static bool IsBoundWithoutBody(ParameterInfo parameter) =>
        HasBindingAttribute(parameter) || IsAutoBoundType(parameter.ParameterType);

    /// <summary>The parameters that take the body when no value is given for them.</summary>
    public static ParameterInfo[] BodyParameters(MethodInfo method) => method.GetParameters().Where(p => !IsBoundWithoutBody(p)).ToArray();

    /// <summary>Invokes the method; returns whether there is a result and the result.</summary>
    public async ValueTask<(bool HasResult, object? Result)> InvokeAsync(object? bean, Exchange exchange)
    {
        var arguments = new object?[_binders.Length];
        for (var i = 0; i < _binders.Length; i++)
        {
            arguments[i] = _binders[i](exchange);
        }

        var returned = _invoke(Method.IsStatic ? null : bean, arguments);
        switch (_resultKind)
        {
            case ResultKind.Void:
                return (false, null);
            case ResultKind.Value:
                return (true, returned);
            case ResultKind.Task:
                if (returned is not null)
                {
                    await _toTask!(returned).ConfigureAwait(false);
                }

                return (false, null);
            default:
                if (returned is null)
                {
                    return (true, null);
                }

                var task = _toTask!(returned);
                await task.ConfigureAwait(false);
                return (true, _taskResult!(task));
        }
    }

    public override string ToString() => AmbiguousMethodCallException.Describe(Method);

    private static bool HasBindingAttribute(ParameterInfo parameter) =>
        parameter.IsDefined(typeof(BodyAttribute)) || parameter.IsDefined(typeof(HeaderAttribute)) || parameter.IsDefined(typeof(HeadersAttribute)) ||
        parameter.IsDefined(typeof(ExchangePropertyAttribute)) || parameter.IsDefined(typeof(ExchangePropertiesAttribute)) ||
        parameter.IsDefined(typeof(ExchangeExceptionAttribute)) || parameter.IsDefined(typeof(SimpleAttribute));

    private static bool IsAutoBoundType(Type type) =>
        type == typeof(Exchange) || type == typeof(Message) || type == typeof(CancellationToken) ||
        typeof(ICaravanContext).IsAssignableFrom(type) || type == typeof(ITypeConverter) || type == typeof(TypeConverterRegistry) ||
        type == typeof(IRegistry);

    private static Func<Exchange, object?> CreateBinder(MethodInfo method, ParameterInfo parameter, BeanParameterToken? token, ref bool bodyBound)
    {
        var type = parameter.ParameterType;
        if (token is not null)
        {
            return token.Kind == BeanParameterTokenKind.Simple
                ? e => Convert(e, method, parameter, token.Expression!.Evaluate(e))
                : e => Convert(e, method, parameter, token.Value);
        }

        if (parameter.GetCustomAttribute<HeaderAttribute>() is { } header)
        {
            var name = header.Name;
            return e => Convert(e, method, parameter, e.Message.GetHeader(name));
        }

        if (parameter.GetCustomAttribute<ExchangePropertyAttribute>() is { } property)
        {
            var name = property.Name;
            return e => Convert(e, method, parameter, e.GetProperty(name));
        }

        if (parameter.GetCustomAttribute<SimpleAttribute>() is { } simple)
        {
            var expression = Language.SimpleLanguage.Expression(simple.Expression);
            return e => Convert(e, method, parameter, expression.Evaluate(e));
        }

        if (parameter.IsDefined(typeof(HeadersAttribute)))
        {
            return e => Convert(e, method, parameter, e.Message.Headers);
        }

        if (parameter.IsDefined(typeof(ExchangePropertiesAttribute)))
        {
            return e => Convert(e, method, parameter, e.Properties);
        }

        if (parameter.IsDefined(typeof(ExchangeExceptionAttribute)))
        {
            return e => FindException(e, type);
        }

        if (parameter.IsDefined(typeof(BodyAttribute)))
        {
            return e => Convert(e, method, parameter, e.Message.Body);
        }

        if (type == typeof(Exchange))
        {
            return e => e;
        }

        if (type == typeof(Message))
        {
            return e => e.Message;
        }

        if (type == typeof(CancellationToken))
        {
            return e => e.CancellationToken;
        }

        if (typeof(ICaravanContext).IsAssignableFrom(type))
        {
            return e => type.IsInstanceOfType(e.Context) ? e.Context : null;
        }

        if (type == typeof(ITypeConverter) || type == typeof(TypeConverterRegistry))
        {
            return e => e.Context.TypeConverter;
        }

        if (type == typeof(IRegistry))
        {
            return e => e.Context.Registry;
        }

        if (!bodyBound)
        {
            bodyBound = true;
            return e => Convert(e, method, parameter, e.Message.Body);
        }

        var fallback = parameter.HasDefaultValue ? parameter.DefaultValue : DefaultOf(type);
        return _ => fallback;
    }

    private static object? FindException(Exchange exchange, Type type)
    {
        var exception = exchange.Exception ?? exchange.GetProperty(ExchangePropertyKeys.ExceptionCaught) as Exception;
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (type.IsInstanceOfType(e))
            {
                return e;
            }
        }

        return null;
    }

    private static object? Convert(Exchange exchange, MethodInfo method, ParameterInfo parameter, object? value)
    {
        var type = parameter.ParameterType;
        if (value is null)
        {
            return DefaultOf(type);
        }

        if (type.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            if (exchange.Context.TypeConverter.TryConvert(type, exchange, value, out var converted))
            {
                return converted ?? DefaultOf(type);
            }
        }
        catch (Exception e)
        {
            throw new ParameterBindingException(e, method, parameter, value);
        }

        throw new ParameterBindingException(new NoTypeConversionAvailableException(value, type), method, parameter, value);
    }

    private static object? DefaultOf(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private static Func<object?, object?[], object?> Compile(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var arguments = Expression.Parameter(typeof(object?[]), "arguments");
        var parameters = method.GetParameters()
            .Select((p, i) => (Expression)Expression.Convert(Expression.ArrayIndex(arguments, Expression.Constant(i)), p.ParameterType))
            .ToArray();
        Expression call = method.IsStatic
            ? Expression.Call(method, parameters)
            : Expression.Call(Expression.Convert(target, method.DeclaringType!), method, parameters);
        Expression body = method.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Constant(null, typeof(object)))
            : Expression.Convert(call, typeof(object));
        return Expression.Lambda<Func<object?, object?[], object?>>(body, target, arguments).Compile();
    }

    private static (ResultKind Kind, Func<object, Task>? ToTask, Func<Task, object?>? TaskResult) AnalyzeReturnType(Type type)
    {
        if (type == typeof(void))
        {
            return (ResultKind.Void, null, null);
        }

        if (type == typeof(Task))
        {
            return (ResultKind.Task, o => (Task)o, null);
        }

        if (type == typeof(ValueTask))
        {
            return (ResultKind.Task, o => ((ValueTask)o).AsTask(), null);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return (ResultKind.TaskOfT, o => (Task)o, CompileResult(type));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = type.GetMethod(nameof(ValueTask<object>.AsTask))!;
            var value = Expression.Parameter(typeof(object), "value");
            var toTask = Expression.Lambda<Func<object, Task>>(Expression.Call(Expression.Convert(value, type), asTask), value).Compile();
            return (ResultKind.TaskOfT, toTask, CompileResult(asTask.ReturnType));
        }

        return (ResultKind.Value, null, null);
    }

    private static Func<Task, object?> CompileResult(Type taskType)
    {
        var task = Expression.Parameter(typeof(Task), "task");
        var result = Expression.Property(Expression.Convert(task, taskType), nameof(Task<object>.Result));
        return Expression.Lambda<Func<Task, object?>>(Expression.Convert(result, typeof(object)), task).Compile();
    }
}
