namespace Caravan.Components.File;

/// <summary>
/// The Simple <c>${file:...}</c> functions (Camel's file language), registered with <see cref="Language.SimpleLanguage.RegisterFunction"/>.
/// They read the file headers a file consumer sets (<c>CaravanFileName</c>, ...), so they also work in a producer's <c>fileName</c> or on a
/// message carrying those headers.
/// </summary>
internal static class FileLanguageFunction
{
    /// <summary>Compiles <c>file:</c><paramref name="function"/>.</summary>
    /// <exception cref="ArgumentException">Unknown function.</exception>
    public static IExpression Parse(string function)
    {
        var text = "file:" + function;
        Func<Exchange, object?> evaluate = function.Trim() switch
        {
            "name" => Name,
            "name.ext" or "ext" => e => Name(e) is { } n ? FileUtil.OnlyExt(n, false) : null,
            "name.ext.single" => e => Name(e) is { } n ? FileUtil.OnlyExt(n, true) : null,
            "name.noext" => e => Name(e) is { } n ? FileUtil.StripExt(n, false) : null,
            "name.noext.single" => e => Name(e) is { } n ? FileUtil.StripExt(n, true) : null,
            "onlyname" => OnlyName,
            "onlyname.noext" => e => OnlyName(e) is { } n ? FileUtil.StripExt(n, false) : null,
            "onlyname.noext.single" => e => OnlyName(e) is { } n ? FileUtil.StripExt(n, true) : null,
            "parent" => e => Header(e, CaravanHeaders.FileParent),
            "path" => e => Header(e, CaravanHeaders.FilePath),
            "absolute" => e => e.Message.GetHeader(CaravanHeaders.FileAbsolute),
            "absolute.path" => e => Header(e, CaravanHeaders.FileAbsolutePath),
            "length" or "size" => e => e.Message.GetHeader(CaravanHeaders.FileLength) is { } length
                ? e.Context.TypeConverter.Convert(typeof(long), e, length)
                : null,
            "modified" => e => e.Message.GetHeader(CaravanHeaders.FileLastModified) switch
            {
                null => null,
                DateTimeOffset d => d,
                DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind)),
                var v => DateTimeOffset.FromUnixTimeMilliseconds((long)e.Context.TypeConverter.Convert(typeof(long), e, v)!),
            },
            _ => throw new ArgumentException(
                $"Unknown file language syntax: {function} (use name, name.ext, name.ext.single, name.noext, name.noext.single, onlyname, " +
                "onlyname.noext, onlyname.noext.single, ext, parent, path, absolute, absolute.path, length, size or modified)"),
        };
        return new FuncExpression(evaluate, text);
    }

    private static string? Header(Exchange exchange, string name) =>
        exchange.Message.GetHeader(name) is { } value ? exchange.Context.TypeConverter.Convert(typeof(string), exchange, value) as string : null;

    private static string? Name(Exchange exchange) => Header(exchange, CaravanHeaders.FileName);

    private static string? OnlyName(Exchange exchange) =>
        Header(exchange, CaravanHeaders.FileNameOnly) ?? (Name(exchange) is { } name ? FileUtil.StripPath(name) : null);
}
