using Caravan.Idempotent;
using Caravan.Language;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The options and behaviour shared by file components (Camel's <c>GenericFileEndpoint</c>): the consumer options (what to pick up, how to
/// lock and what to do with the file afterwards), the producer options (names, what to do when the file exists, temporary files, done
/// files), and the scheduling options of <see cref="ScheduledPollEndpoint"/>. A component for another file system derives from it with its
/// file type and supplies <see cref="CreateOperations"/>; <see cref="FileEndpoint"/> is the local one. Options taking expressions are Simple
/// texts compiled when set.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public abstract class GenericFileEndpoint<T> : ScheduledPollEndpoint
{
    /// <summary>The default directory, next to the consumed file, consumed files are moved to (Camel's <c>.camel</c>).</summary>
    public const string DefaultMoveDirectory = ".caravan";

    /// <summary>The default capacity of the in-progress repository (as Camel).</summary>
    public const int DefaultInProgressCacheSize = 50000;

    private readonly Lock _lock = new();
    private string? _move;
    private string? _preMove;
    private string? _moveFailed;
    private string? _fileName;
    private string? _tempFileName;
    private string? _tempPrefix;
    private string? _moveExisting;
    private string? _idempotentKey;
    private string? _filterFile;
    private string? _filterDirectory;
    private string? _sortBy;
    private string? _include;
    private string? _exclude;
    private string? _antInclude;
    private string? _antExclude;
    private string? _appendChars;
    private IGenericFileProcessStrategy<T>? _effectiveProcessStrategy;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="endpointUri">The endpoint URI.</param>
    /// <param name="component">The component.</param>
    /// <param name="context">The context.</param>
    /// <param name="directory">The starting directory (the URI path).</param>
    protected GenericFileEndpoint(string endpointUri, IComponent? component, ICaravanContext context, string directory)
        : base(endpointUri, component, context)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The file endpoint requires a directory, e.g. file:inbox");
        }

        Directory = FileUtil.StripTrailingSeparator(FileUtil.Normalize(directory));
    }

    /// <summary>The starting directory, as configured (relative to the current directory unless absolute).</summary>
    public string Directory { get; }

    /// <summary>The full path of the starting directory.</summary>
    public string AbsoluteDirectory => Path.GetFullPath(Directory);

    // ---- common ----

    /// <summary>The charset of text content: read by converting a consumed file to a string, written for string bodies (default UTF-8).</summary>
    public string? Charset { get; set; }

    /// <summary>
    /// Consumer: only the file with this name is taken. Producer: the name to write (relative to the starting directory), instead of the
    /// <c>CaravanFileName</c> header. A Simple expression (<c>${date:now:yyyyMMdd}/report.txt</c>).
    /// </summary>
    public string? FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            FileNameExpression = value is null ? null : SimpleLanguage.Expression(value);
        }
    }

    /// <summary>The compiled <see cref="FileName"/>.</summary>
    public IExpression? FileNameExpression { get; private set; }

    /// <summary>
    /// The name of the done file: a consumer only takes a file once its done file exists (and deletes it afterwards), a producer writes it after
    /// the file. Only <c>${file:name}</c> and <c>${file:name.noext}</c> may be used (the done file is in the file's directory).
    /// </summary>
    public string? DoneFileName { get; set; }

    /// <summary>
    /// Consumer: <c>CaravanFileName</c> holds the name without directories. Producer: directories in the name are removed, so every file goes to the
    /// starting directory.
    /// </summary>
    public bool Flatten { get; set; }

    /// <summary>Consumer: create the starting directory when it is missing; producer: create missing directories (default true).</summary>
    public bool AutoCreate { get; set; } = true;

    // ---- consumer ----

    /// <summary>Delete the file after successful processing.</summary>
    public bool Delete { get; set; }

    /// <summary>Leave the file where it is (implies <see cref="Idempotent"/> unless set).</summary>
    public bool Noop { get; set; }

    /// <summary>
    /// Where to move the file after successful processing (Simple; a plain directory such as <c>done</c> means <c>${file:parent}/done/${file:onlyname}</c>).
    /// Default <c>.caravan</c> next to the file, unless <see cref="Noop"/> or <see cref="Delete"/>.
    /// </summary>
    public string? Move
    {
        get => _move;
        set
        {
            _move = value;
            MoveExpression = value is null ? null : SimpleLanguage.Expression(ConfigureMoveExpression(value));
        }
    }

    /// <summary>The compiled <see cref="Move"/>.</summary>
    public IExpression? MoveExpression { get; private set; }

    /// <summary>Where to move the file before processing (as <see cref="Move"/>); the exchange then refers to the moved file.</summary>
    public string? PreMove
    {
        get => _preMove;
        set
        {
            _preMove = value;
            PreMoveExpression = value is null ? null : SimpleLanguage.Expression(ConfigureMoveExpression(value));
        }
    }

    /// <summary>The compiled <see cref="PreMove"/>.</summary>
    public IExpression? PreMoveExpression { get; private set; }

    /// <summary>Where to move the file when the exchange failed (as <see cref="Move"/>); without it the file is retried at the next poll.</summary>
    public string? MoveFailed
    {
        get => _moveFailed;
        set
        {
            _moveFailed = value;
            MoveFailedExpression = value is null ? null : SimpleLanguage.Expression(ConfigureMoveExpression(value));
        }
    }

    /// <summary>The compiled <see cref="MoveFailed"/>.</summary>
    public IExpression? MoveFailedExpression { get; private set; }

    /// <summary>Also take the files of sub-directories.</summary>
    public bool Recursive { get; set; }

    /// <summary>The minimum depth of the files taken: 1 is the starting directory, 2 its sub-directories (default 0: all).</summary>
    public int MinDepth { get; set; }

    /// <summary>The maximum depth of the files taken (default unlimited).</summary>
    public int MaxDepth { get; set; } = int.MaxValue;

    /// <summary>Only take files whose name (without directories) matches this regular expression (ignoring case).</summary>
    public string? Include
    {
        get => _include;
        set
        {
            _include = value;
            IncludePattern = value is null ? null : new System.Text.RegularExpressions.Regex($"^(?:{value})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    /// <summary>The compiled <see cref="Include"/>.</summary>
    public System.Text.RegularExpressions.Regex? IncludePattern { get; private set; }

    /// <summary>Skip files whose name (without directories) matches this regular expression (ignoring case); wins over <see cref="Include"/>.</summary>
    public string? Exclude
    {
        get => _exclude;
        set
        {
            _exclude = value;
            ExcludePattern = value is null ? null : new System.Text.RegularExpressions.Regex($"^(?:{value})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    /// <summary>The compiled <see cref="Exclude"/>.</summary>
    public System.Text.RegularExpressions.Regex? ExcludePattern { get; private set; }

    /// <summary>Only take files with one of these extensions (comma-separated, ignoring case, e.g. <c>txt,xml</c> or <c>tar.gz</c>).</summary>
    public string? IncludeExt { get; set; }

    /// <summary>Skip files with one of these extensions (comma-separated, ignoring case); wins over <see cref="IncludeExt"/>.</summary>
    public string? ExcludeExt { get; set; }

    /// <summary>Only take files whose path relative to the starting directory matches one of these Ant patterns (comma-separated, <c>**/*.txt</c>).</summary>
    public string? AntInclude
    {
        get => _antInclude;
        set
        {
            _antInclude = value;
            UpdateAntFilter();
        }
    }

    /// <summary>Skip files whose relative path matches one of these Ant patterns (comma-separated); wins over <see cref="AntInclude"/>.</summary>
    public string? AntExclude
    {
        get => _antExclude;
        set
        {
            _antExclude = value;
            UpdateAntFilter();
        }
    }

    /// <summary>Whether the Ant patterns are case-sensitive (default true).</summary>
    public bool AntFilterCaseSensitive
    {
        get;
        set
        {
            field = value;
            UpdateAntFilter();
        }
    } = true;

    /// <summary>The filter built from <see cref="AntInclude"/> and <see cref="AntExclude"/>, or null.</summary>
    public AntPathMatcherGenericFileFilter<T>? AntFilter { get; private set; }

    /// <summary>A custom filter (<c>filter=#bean</c>).</summary>
    public IGenericFileFilter<T>? Filter { get; set; }

    /// <summary>A Simple predicate files must match, evaluated on an exchange carrying the file's headers (<c>${file:size} &gt; 100</c>).</summary>
    public string? FilterFile
    {
        get => _filterFile;
        set
        {
            _filterFile = value;
            FilterFilePredicate = value is null ? null : SimpleLanguage.Predicate(value);
        }
    }

    /// <summary>The compiled <see cref="FilterFile"/>.</summary>
    public IPredicate? FilterFilePredicate { get; private set; }

    /// <summary>A Simple predicate directories must match to be entered with <see cref="Recursive"/>.</summary>
    public string? FilterDirectory
    {
        get => _filterDirectory;
        set
        {
            _filterDirectory = value;
            FilterDirectoryPredicate = value is null ? null : SimpleLanguage.Predicate(value);
        }
    }

    /// <summary>The compiled <see cref="FilterDirectory"/>.</summary>
    public IPredicate? FilterDirectoryPredicate { get; private set; }

    /// <summary>Whether to take files whose name starts with a dot (default false).</summary>
    public bool IncludeHiddenFiles { get; set; }

    /// <summary>Whether to enter directories whose name starts with a dot with <see cref="Recursive"/> (default false).</summary>
    public bool IncludeHiddenDirs { get; set; }

    /// <summary>
    /// The order files are processed in: <c>file:name</c>, <c>file:modified</c>, ... (Simple functions, with or without <c>${}</c>), each optionally
    /// prefixed with <c>reverse:</c> and/or <c>ignoreCase:</c>, groups separated by <c>;</c> (<c>file:ext;reverse:file:modified</c>).
    /// </summary>
    public string? SortBy
    {
        get => _sortBy;
        set
        {
            _sortBy = value;
            SortByComparer = value is null ? null : GenericFileSorter.Create(value);
        }
    }

    /// <summary>The comparer built from <see cref="SortBy"/>, comparing exchanges created for files.</summary>
    public IComparer<Exchange>? SortByComparer { get; private set; }

    /// <summary>A custom order of the files (<c>sorter=#bean</c>), applied before <see cref="SortBy"/>.</summary>
    public IComparer<GenericFile<T>>? Sorter { get; set; }

    /// <summary>Process the files of a poll in random order.</summary>
    public bool Shuffle { get; set; }

    /// <summary>Sort the entries of each directory by name while listing (before <see cref="EagerMaxMessagesPerPoll"/> applies).</summary>
    public bool PreSort { get; set; }

    /// <summary>The maximum number of files processed per poll (default 0: unlimited).</summary>
    public int MaxMessagesPerPoll { get; set; }

    /// <summary>
    /// True (default): listing stops once <see cref="MaxMessagesPerPoll"/> files were found, so sorting applies to those only; false: all files
    /// are listed and sorted, then the first ones taken.
    /// </summary>
    public bool EagerMaxMessagesPerPoll { get; set; } = true;

    /// <summary>Skip files already processed, by <see cref="IdempotentKey"/> in <see cref="IdempotentRepository"/> (default: as <see cref="Noop"/>).</summary>
    public bool? Idempotent { get; set; }

    /// <summary>Whether the consumer is idempotent (<see cref="Idempotent"/>, defaulting to <see cref="Noop"/>).</summary>
    public bool IsIdempotent => Idempotent ?? Noop;

    /// <summary>The key of a file in <see cref="IdempotentRepository"/> (Simple; default the absolute path, as Camel), e.g. <c>${file:name}-${file:modified}</c>.</summary>
    public string? IdempotentKey
    {
        get => _idempotentKey;
        set
        {
            _idempotentKey = value;
            IdempotentKeyExpression = value is null ? null : SimpleLanguage.Expression(value);
        }
    }

    /// <summary>The compiled <see cref="IdempotentKey"/>.</summary>
    public IExpression? IdempotentKeyExpression { get; private set; }

    /// <summary>The repository of processed files (default an in-memory one of 1000 keys, created when needed).</summary>
    public IIdempotentRepository? IdempotentRepository { get; set; }

    /// <summary>The files being processed, so that no file is taken twice (default in-memory, 50000 keys).</summary>
    public IIdempotentRepository InProgressRepository { get; set; } = new MemoryIdempotentRepository(DefaultInProgressCacheSize);

    /// <summary>Fail polls when the starting directory does not exist (default false: nothing to do).</summary>
    public bool DirectoryMustExist { get; set; }

    /// <summary>Fail the consumer's start when the starting directory does not exist and is not created (<see cref="AutoCreate"/>).</summary>
    public bool StartingDirectoryMustExist { get; set; }

    /// <summary>A custom process strategy (<c>processStrategy=#bean</c>), replacing <c>noop</c>/<c>delete</c>/<c>move</c>/<c>readLock</c>.</summary>
    public IGenericFileProcessStrategy<T>? ProcessStrategy { get; set; }

    /// <summary>Handles failures of the work done after processing (moving, deleting); default: logged as warnings.</summary>
    public IExceptionHandler? OnCompletionExceptionHandler { get; set; }

    /// <summary>The read lock: <c>none</c>, <c>markerFile</c> (default), <c>changed</c>, <c>fileLock</c>, <c>rename</c> or <c>idempotent</c>.</summary>
    public string ReadLock { get; set; } = "markerFile";

    /// <summary>How long <c>changed</c>, <c>fileLock</c> and <c>rename</c> try before skipping the file until the next poll (default 10s; 0 = forever).</summary>
    public TimeSpan ReadLockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The interval between the attempts of <c>changed</c>, <c>fileLock</c> and <c>rename</c> (default 1s).</summary>
    public TimeSpan ReadLockCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The minimum length of a file for <c>changed</c> (default 1).</summary>
    public long ReadLockMinLength { get; set; } = 1;

    /// <summary>For <c>changed</c>: take a file once it was last modified at least this long ago (default zero: compare two checks).</summary>
    public TimeSpan ReadLockMinAge { get; set; }

    /// <summary>The level of the message logged when a file is skipped because no read lock was acquired (default debug).</summary>
    public LogLevel ReadLockLoggingLevel { get; set; } = LogLevel.Debug;

    /// <summary>Whether <c>changed</c>, <c>fileLock</c> and <c>rename</c> also use a marker file (default true).</summary>
    public bool ReadLockMarkerFile { get; set; } = true;

    /// <summary>Whether marker files left by a crash are deleted before the first poll (default true).</summary>
    public bool ReadLockDeleteOrphanLockFiles { get; set; } = true;

    /// <summary>For <c>idempotent</c>: remove the key when the exchange failed (default true).</summary>
    public bool ReadLockRemoveOnRollback { get; set; } = true;

    /// <summary>For <c>idempotent</c>: remove the key when the exchange succeeded (default false).</summary>
    public bool ReadLockRemoveOnCommit { get; set; }

    /// <summary>A custom read lock (<c>exclusiveReadLockStrategy=#bean</c>), replacing <see cref="ReadLock"/>.</summary>
    public IGenericFileExclusiveReadLockStrategy<T>? ExclusiveReadLockStrategy { get; set; }

    // ---- producer ----

    /// <summary>What to do when the file to write exists (default <see cref="GenericFileExist.Override"/>).</summary>
    public GenericFileExist FileExist { get; set; } = GenericFileExist.Override;

    /// <summary>
    /// With <see cref="GenericFileExist.Move"/>: where to move the existing file (Simple, as <see cref="Move"/>; evaluated with <c>${file:name}</c>,
    /// <c>${file:onlyname}</c> and <c>${file:parent}</c> of the existing file).
    /// </summary>
    public string? MoveExisting
    {
        get => _moveExisting;
        set
        {
            _moveExisting = value;
            MoveExistingExpression = value is null ? null : SimpleLanguage.Expression(ConfigureMoveExpression(value));
        }
    }

    /// <summary>The compiled <see cref="MoveExisting"/>.</summary>
    public IExpression? MoveExistingExpression { get; private set; }

    /// <summary>Write to a temporary file named with this prefix, then rename it (so consumers never see a partial file).</summary>
    public string? TempPrefix
    {
        get => _tempPrefix;
        set
        {
            _tempPrefix = value;
            if (value is not null)
            {
                TempFileName = value + "${file:onlyname}";
            }
        }
    }

    /// <summary>Write to this temporary file name (Simple, in the target's directory), then rename it.</summary>
    public string? TempFileName
    {
        get => _tempFileName;
        set
        {
            _tempFileName = value;
            TempFileNameExpression = value is null ? null : SimpleLanguage.Expression(value);
        }
    }

    /// <summary>The compiled <see cref="TempFileName"/>.</summary>
    public IExpression? TempFileNameExpression { get; private set; }

    /// <summary>With a temporary file and <see cref="GenericFileExist.Override"/>: delete the existing target before writing (default true) or just before renaming.</summary>
    public bool EagerDeleteTargetFile { get; set; } = true;

    /// <summary>Set the written file's modification time from the <c>CaravanFileLastModified</c> header.</summary>
    public bool KeepLastModified { get; set; }

    /// <summary>Write an empty file for a null body (default false: fail).</summary>
    public bool AllowNullBody { get; set; }

    /// <summary>Text appended after each write (<c>\n</c>, <c>\r</c> and <c>\t</c> escapes; in URIs <c>\\n</c>), e.g. to separate appended records.</summary>
    public string? AppendChars
    {
        get => _appendChars;
        set => _appendChars = value is null ? null : FileUtil.Unescape(value);
    }

    /// <summary>
    /// Only write files inside the starting directory (default true): a name leading outside it (<c>../x</c>, an absolute path) fails, so a header
    /// coming from outside cannot write anywhere.
    /// </summary>
    public bool JailStartingDirectory { get; set; } = true;

    /// <summary>Flush written files to the storage device before completing (default true, as Camel's <c>forceWrites</c>).</summary>
    public bool ForceWrites { get; set; } = true;

    /// <summary>The process strategy in use: <see cref="ProcessStrategy"/>, or the one built from the options.</summary>
    public IGenericFileProcessStrategy<T> GenericFileProcessStrategy
    {
        get
        {
            lock (_lock)
            {
                return _effectiveProcessStrategy ??= ProcessStrategy ?? CreateProcessStrategy();
            }
        }
    }

    /// <inheritdoc />
    public override IProducer CreateProducer() => new GenericFileProducer<T>(this, CreateOperations());

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor)
    {
        var consumer = new GenericFileConsumer<T>(this, processor, CreateOperations());
        ConfigureConsumer(consumer);
        return consumer;
    }

    /// <summary>Creates the exchange for a listed file: the file is the body, its headers are set (Camel's <c>createExchange</c>).</summary>
    /// <param name="file">The file.</param>
    public Exchange CreateExchange(GenericFile<T> file)
    {
        var exchange = CreateExchange();
        BindToExchange(exchange, file);
        return exchange;
    }

    /// <summary>Binds <paramref name="file"/> to <paramref name="exchange"/> as <see cref="CreateExchange(GenericFile{T})"/> does.</summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="file">The file.</param>
    public void BindToExchange(Exchange exchange, GenericFile<T> file)
    {
        file.BindToExchange(exchange);
        if (Flatten)
        {
            exchange.Message.SetHeader(CaravanHeaders.FileName, file.FileNameOnly);
        }

        if (Charset is not null)
        {
            exchange.SetProperty(ExchangePropertyKeys.CharsetName, Charset);
        }
    }

    /// <summary>The name of the done file of <paramref name="fileName"/> (a path): in the same directory (Camel's <c>createDoneFileName</c>).</summary>
    /// <param name="fileName">The file.</param>
    /// <exception cref="ArgumentException">The done file name uses functions other than <c>${file:name}</c> and <c>${file:name.noext}</c>.</exception>
    public string CreateDoneFileName(string fileName)
    {
        var pattern = DoneFileName ?? throw new InvalidOperationException("No doneFileName configured");
        var path = FileUtil.OnlyPath(fileName);
        var onlyName = FileUtil.StripPath(fileName);
        pattern = pattern
            .Replace("${file:name.noext}", FileUtil.StripExt(onlyName, true), StringComparison.Ordinal)
            .Replace("$simple{file:name.noext}", FileUtil.StripExt(onlyName, true), StringComparison.Ordinal)
            .Replace("${file:name}", onlyName, StringComparison.Ordinal)
            .Replace("$simple{file:name}", onlyName, StringComparison.Ordinal);
        if (pattern.Contains("${", StringComparison.Ordinal) || pattern.Contains("$simple{", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Cannot resolve the done file name {DoneFileName} for {fileName}: only ${{file:name}} and ${{file:name.noext}} are supported");
        }

        return string.IsNullOrEmpty(path) ? pattern : Path.Combine(path, pattern);
    }

    /// <summary>Whether <paramref name="fileNameOnly"/> is the name of a done file (Camel's <c>isDoneFile</c>).</summary>
    /// <param name="fileNameOnly">A file name without directories.</param>
    public bool IsDoneFile(string fileNameOnly)
    {
        if (DoneFileName is null)
        {
            return false;
        }

        var pattern = DoneFileName.Replace("$simple{", "${", StringComparison.Ordinal);
        if (!pattern.Contains("${", StringComparison.Ordinal))
        {
            return string.Equals(pattern, fileNameOnly, StringComparison.Ordinal);
        }

        var regex = System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(System.Text.RegularExpressions.Regex.Escape("${file:name.noext}"), "(.+)", StringComparison.Ordinal)
            .Replace(System.Text.RegularExpressions.Regex.Escape("${file:name}"), "(.+)", StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.IsMatch(fileNameOnly, "^" + regex + "$");
    }

    /// <summary>Checks option combinations; called when the endpoint is created.</summary>
    /// <exception cref="ArgumentException">Conflicting or invalid options.</exception>
    public virtual void Validate()
    {
        if (Delete && Move is not null)
        {
            throw new ArgumentException("You cannot set both delete=true and move options");
        }

        if (FileExist == GenericFileExist.Append && TempFileName is not null)
        {
            throw new ArgumentException("You cannot set both fileExist=Append and tempPrefix/tempFileName options");
        }

        if (FileExist == GenericFileExist.Move && MoveExisting is null)
        {
            throw new ArgumentException("You must configure the moveExisting option when fileExist=Move");
        }

        if (MinDepth > MaxDepth)
        {
            throw new ArgumentException($"minDepth ({MinDepth}) must not be greater than maxDepth ({MaxDepth})");
        }

        var readLock = ReadLock.ToLowerInvariant();
        if (readLock is not ("none" or "markerfile" or "changed" or "filelock" or "rename" or "idempotent"))
        {
            throw new ArgumentException($"Unknown readLock '{ReadLock}': use none, markerFile, changed, fileLock, rename or idempotent");
        }

        if (readLock is "changed" or "filelock" or "rename" && ReadLockTimeout > TimeSpan.Zero && ReadLockTimeout <= ReadLockCheckInterval)
        {
            throw new ArgumentException(
                $"The option readLockTimeout must be higher than readLockCheckInterval, was readLockTimeout={ReadLockTimeout.TotalMilliseconds}, " +
                $"readLockCheckInterval={ReadLockCheckInterval.TotalMilliseconds}. A good practice is to let the readLockTimeout be at least 3 times higher " +
                "than the readLockCheckInterval, to ensure that the read lock procedure has enough time to acquire the lock.");
        }

        if (DoneFileName is not null)
        {
            CreateDoneFileName("probe.txt");
        }
    }

    /// <summary>The file operations of this component (one instance per consumer or producer).</summary>
    protected internal abstract IGenericFileOperations<T> CreateOperations();

    /// <summary>
    /// Builds the read lock from <see cref="ReadLock"/> (Camel's factory). The generic implementation supports <c>none</c>, <c>rename</c> and
    /// <c>idempotent</c>; components add the others for their file type.
    /// </summary>
    protected virtual IGenericFileExclusiveReadLockStrategy<T>? CreateExclusiveReadLockStrategy() => ReadLock.ToLowerInvariant() switch
    {
        "none" => null,
        "rename" => new GenericFileRenameExclusiveReadLockStrategy<T>
        {
            Timeout = ReadLockTimeout,
            CheckInterval = ReadLockCheckInterval,
            ReadLockLoggingLevel = ReadLockLoggingLevel,
        },
        "idempotent" => new GenericFileIdempotentRepositoryReadLockStrategy<T>(GetOrCreateIdempotentRepository())
        {
            RemoveOnRollback = ReadLockRemoveOnRollback,
            RemoveOnCommit = ReadLockRemoveOnCommit,
            ReadLockLoggingLevel = ReadLockLoggingLevel,
        },
        _ => throw new NotSupportedException($"readLock={ReadLock} is not supported by {GetType().Name}"),
    };

    /// <summary>Builds the process strategy from the options (Camel's <c>GenericFileProcessStrategyFactory</c>).</summary>
    protected virtual IGenericFileProcessStrategy<T> CreateProcessStrategy()
    {
        var readLock = ExclusiveReadLockStrategy ?? CreateExclusiveReadLockStrategy();
        if (Noop)
        {
            return new GenericFileNoOpProcessStrategy<T> { ExclusiveReadLockStrategy = readLock };
        }

        if (Delete)
        {
            return new GenericFileDeleteProcessStrategy<T>
            {
                ExclusiveReadLockStrategy = readLock,
                BeginRenamer = PreMoveExpression,
                FailureRenamer = MoveFailedExpression,
            };
        }

        return new GenericFileRenameProcessStrategy<T>
        {
            ExclusiveReadLockStrategy = readLock,
            BeginRenamer = PreMoveExpression,
            CommitRenamer = MoveExpression ?? SimpleLanguage.Expression("${file:parent}/" + DefaultMoveDirectory + "/${file:onlyname}"),
            FailureRenamer = MoveFailedExpression,
        };
    }

    /// <summary>The idempotent repository, created (in memory) when none is set.</summary>
    protected internal IIdempotentRepository GetOrCreateIdempotentRepository()
    {
        lock (_lock)
        {
            return IdempotentRepository ??= new MemoryIdempotentRepository();
        }
    }

    // A plain directory becomes ${file:parent}/dir/${file:onlyname} (Camel's configureMoveOrPreMoveExpression).
    private static string ConfigureMoveExpression(string expression)
    {
        if (expression.Contains("${", StringComparison.Ordinal) || expression.Contains("$simple{", StringComparison.Ordinal))
        {
            return expression;
        }

        var directory = FileUtil.StripTrailingSeparator(expression);
        return (Path.IsPathRooted(directory) ? string.Empty : "${file:parent}/") + directory + "/${file:onlyname}";
    }

    private void UpdateAntFilter() =>
        AntFilter = _antInclude is null && _antExclude is null ? null : new AntPathMatcherGenericFileFilter<T>(_antInclude, _antExclude, AntFilterCaseSensitive);
}
