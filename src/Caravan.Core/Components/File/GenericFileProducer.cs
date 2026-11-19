using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The producer of file components (Camel's <c>GenericFileProducer</c>): writes the body to the file named by <c>CaravanOverruleFileName</c>,
/// the <c>fileName</c> option or <c>CaravanFileName</c> (else the message id), inside the starting directory (<c>jailStartingDirectory</c>),
/// applying <c>fileExist</c>, the temporary file (<c>tempPrefix</c>/<c>tempFileName</c>, renamed when written) and the done file, then sets
/// <c>CaravanFileNameProduced</c>. Writes to the same file are serialized.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public class GenericFileProducer<T> : ProducerBase
{
    private static readonly SemaphoreSlim[] Locks = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];
    private readonly GenericFileEndpoint<T> _endpoint;
    private readonly ILogger _logger;

    /// <summary>Creates the producer.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="operations">The file operations.</param>
    public GenericFileProducer(GenericFileEndpoint<T> endpoint, IGenericFileOperations<T> operations)
        : base(endpoint)
    {
        _endpoint = endpoint;
        Operations = operations;
        _logger = endpoint.Context.LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>The file operations.</summary>
    public IGenericFileOperations<T> Operations { get; }

    /// <inheritdoc />
    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        var hadFileName = message.Headers.TryGetValue(CaravanHeaders.FileName, out var existing);
        try
        {
            var target = CreateFileName(exchange);
            var gate = Locks[(uint)StringComparer.Ordinal.GetHashCode(target) % (uint)Locks.Length];
            await gate.WaitAsync(exchange.CancellationToken).ConfigureAwait(false);
            try
            {
                await WriteAsync(exchange, target).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            // The overruling name is used once; the original name is kept for the rest of the route.
            message.RemoveHeader(CaravanHeaders.OverruleFileName);
            if (hadFileName)
            {
                message.SetHeader(CaravanHeaders.FileName, existing);
            }
            else
            {
                message.RemoveHeader(CaravanHeaders.FileName);
            }
        }
    }

    /// <summary>
    /// The full path of the file to write for <paramref name="exchange"/> (Camel's <c>createFileName</c>).
    /// </summary>
    /// <param name="exchange">The exchange.</param>
    /// <exception cref="ArgumentException">The name leads outside the starting directory and <c>jailStartingDirectory</c> is on.</exception>
    public string CreateFileName(Exchange exchange)
    {
        var message = exchange.Message;
        var converter = exchange.Context.TypeConverter;
        object? value;
        var overrule = message.GetHeader(CaravanHeaders.OverruleFileName);
        if (overrule is not null)
        {
            value = overrule is IExpression ? overrule : converter.Convert(typeof(string), exchange, overrule);
            message.SetHeader(CaravanHeaders.FileName, value);
        }
        else
        {
            value = message.GetHeader(CaravanHeaders.FileName);
        }

        var expression = value as IExpression ?? _endpoint.FileNameExpression;
        var name = expression is not null
            ? converter.Convert(typeof(string), exchange, expression.Evaluate(exchange)) as string
            : converter.Convert(typeof(string), exchange, value) as string;
        if (string.IsNullOrEmpty(name))
        {
            name = message.MessageId;
        }
        else if (_endpoint.Flatten)
        {
            name = FileUtil.StripPath(name);
        }

        name = FileUtil.Normalize(name);
        var path = Path.GetFullPath(Path.IsPathRooted(name) ? name : Path.Combine(_endpoint.Directory, name));
        Jail(path, name);
        return path;
    }

    private void Jail(string path, string name)
    {
        if (_endpoint.JailStartingDirectory && (!FileUtil.IsUnder(path, _endpoint.AbsoluteDirectory) || path == Path.TrimEndingDirectorySeparator(_endpoint.AbsoluteDirectory)))
        {
            throw new ArgumentException(
                $"Cannot write file with name: {name} as the filename is jailed to the starting directory: {_endpoint.Directory}");
        }
    }

    private async Task WriteAsync(Exchange exchange, string target)
    {
        var cancellationToken = exchange.CancellationToken;
        var temp = _endpoint.TempFileNameExpression is not null ? CreateTempFileName(exchange, target) : null;
        var exists = Operations.ExistsFile(target);
        if (exists)
        {
            switch (_endpoint.FileExist)
            {
                case GenericFileExist.Ignore:
                    _logger.LogDebug("An existing file already exists: {File}. Ignore and do not override it.", target);
                    return;
                case GenericFileExist.Fail:
                    throw new GenericFileOperationFailedException($"File already exist: {target}. Cannot write new file.");
                case GenericFileExist.Move:
                    MoveExistingFile(exchange, target);
                    exists = false;
                    break;
                case GenericFileExist.Override when temp is not null && _endpoint.EagerDeleteTargetFile:
                    Operations.DeleteFile(target);
                    exists = false;
                    break;
            }
        }

        if (temp is not null && Operations.ExistsFile(temp))
        {
            Operations.DeleteFile(temp);
        }

        var writeTo = temp ?? target;
        if (_endpoint.AutoCreate && Path.GetDirectoryName(writeTo) is { Length: > 0 } directory && !Operations.BuildDirectory(directory, true))
        {
            _logger.LogDebug("Cannot build directory {Directory} (could be because of denied permissions)", directory);
        }

        var append = _endpoint.FileExist == GenericFileExist.Append;
        if (!await Operations.StoreFileAsync(writeTo, exchange, append, cancellationToken).ConfigureAwait(false))
        {
            throw new GenericFileOperationFailedException($"Error writing file [{writeTo}]");
        }

        if (temp is not null)
        {
            if (exists && _endpoint.FileExist == GenericFileExist.Override && !_endpoint.EagerDeleteTargetFile)
            {
                Operations.DeleteFile(target);
            }

            if (!Operations.RenameFile(temp, target))
            {
                throw new GenericFileOperationFailedException($"Cannot rename file from: {temp} to: {target}");
            }
        }

        if (_endpoint.DoneFileName is not null)
        {
            var doneFile = _endpoint.CreateDoneFileName(target);
            var empty = new Exchange(exchange.Context);
            empty.Message.Body = Array.Empty<byte>();
            if (!await Operations.StoreFileAsync(doneFile, empty, append: false, cancellationToken).ConfigureAwait(false))
            {
                throw new GenericFileOperationFailedException($"Cannot store done file: {doneFile}");
            }
        }

        exchange.Message.SetHeader(CaravanHeaders.FileNameProduced, target);
    }

    // The temporary name is evaluated with the target's name in CaravanFileName/CaravanFileNameOnly, and lives in the target's directory.
    private string CreateTempFileName(Exchange exchange, string target)
    {
        var message = exchange.Message;
        var onlyName = FileUtil.StripPath(target);
        var hadName = message.Headers.TryGetValue(CaravanHeaders.FileName, out var name);
        var hadOnly = message.Headers.TryGetValue(CaravanHeaders.FileNameOnly, out var only);
        string? tempName;
        try
        {
            message.SetHeader(CaravanHeaders.FileName, onlyName);
            message.SetHeader(CaravanHeaders.FileNameOnly, onlyName);
            tempName = exchange.Context.TypeConverter.Convert(typeof(string), exchange, _endpoint.TempFileNameExpression!.Evaluate(exchange)) as string;
        }
        finally
        {
            Restore(message, CaravanHeaders.FileName, hadName, name);
            Restore(message, CaravanHeaders.FileNameOnly, hadOnly, only);
        }

        if (string.IsNullOrEmpty(tempName))
        {
            throw new GenericFileOperationFailedException($"The tempFileName {_endpoint.TempFileName} gave no name for {target}");
        }

        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target) ?? _endpoint.AbsoluteDirectory, FileUtil.Normalize(tempName)));
        Jail(path, tempName);
        return path;
    }

    private void MoveExistingFile(Exchange exchange, string target)
    {
        var dummy = _endpoint.CreateExchange();
        dummy.Message.SetHeader(CaravanHeaders.FileName, target);
        dummy.Message.SetHeader(CaravanHeaders.FileNameOnly, FileUtil.StripPath(target));
        dummy.Message.SetHeader(CaravanHeaders.FileParent, FileUtil.OnlyPath(target));
        var to = exchange.Context.TypeConverter.Convert(typeof(string), dummy, _endpoint.MoveExistingExpression!.Evaluate(dummy)) as string;
        if (string.IsNullOrEmpty(to))
        {
            throw new GenericFileOperationFailedException($"moveExisting evaluated as empty for {target}");
        }

        to = FileUtil.Normalize(to);
        to = Path.GetFullPath(Path.IsPathRooted(to) ? to : Path.Combine(_endpoint.Directory, to));
        if (Path.GetDirectoryName(to) is { Length: > 0 } directory && !Operations.BuildDirectory(directory, true))
        {
            throw new GenericFileOperationFailedException($"Cannot create directory: {directory} (could be because of denied permissions)");
        }

        if (Operations.ExistsFile(to))
        {
            if (!_endpoint.EagerDeleteTargetFile)
            {
                throw new GenericFileOperationFailedException($"Cannot move existing file from: {target} to: {to} as there already exists a file: {to}");
            }

            Operations.DeleteFile(to);
        }

        if (!Operations.RenameFile(target, to))
        {
            throw new GenericFileOperationFailedException($"Cannot rename existing file from: {target} to: {to}");
        }
    }

    private static void Restore(Message message, string header, bool had, object? value)
    {
        if (had)
        {
            message.SetHeader(header, value);
        }
        else
        {
            message.RemoveHeader(header);
        }
    }
}
