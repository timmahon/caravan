using Caravan.Builder;
using Caravan.Components.File;
using Caravan.Idempotent;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class FileConsumerTests : FileTestBase
{
    [Fact]
    public async Task Consumes_a_file_and_moves_it_to_the_default_directory()
    {
        WriteFile("in/hello.txt", "Hello World");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("Hello World");
        await StartWithRoutes(b => b.From(From("in")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/hello.txt"], FilesIn("in"));
        Assert.Equal("Hello World", ReadFile("in/.caravan/hello.txt"));
    }

    [Fact]
    public async Task Default_options_follow_camel()
    {
        await Context.StartAsync();
        var endpoint = Endpoint(To("defaults"));

        Assert.Equal(TimeSpan.FromSeconds(1), endpoint.InitialDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(500), endpoint.Delay);
        Assert.Equal("markerFile", endpoint.ReadLock);
        Assert.Equal(TimeSpan.FromSeconds(10), endpoint.ReadLockTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), endpoint.ReadLockCheckInterval);
        Assert.True(endpoint.AutoCreate);
        Assert.True(endpoint.EagerMaxMessagesPerPoll);
        Assert.True(endpoint.JailStartingDirectory);
        Assert.Equal(GenericFileExist.Override, endpoint.FileExist);
        Assert.False(endpoint.IsIdempotent);
        Assert.Equal(int.MaxValue, endpoint.MaxDepth);
        Assert.IsType<GenericFileRenameProcessStrategy<FileInfo>>(endpoint.GenericFileProcessStrategy);
    }

    [Fact]
    public async Task Sets_the_file_headers_relative_to_a_relative_starting_directory()
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, PathOf("in"));
        var path = WriteFile("in/sub/hello.txt", "Hello", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From($"file:{relative}?initialDelay=0&delay=1000&recursive=true").To("mock:result"));
        await FirstPollAsync();
        await mock.AssertIsSatisfiedAsync();

        var message = mock.ReceivedExchanges[0].Message;
        var sub = Path.Combine("sub", "hello.txt");
        Assert.Equal(sub, message.GetHeader(CaravanHeaders.FileName));
        Assert.Equal(sub, message.GetHeader(CaravanHeaders.FileNameConsumed));
        Assert.Equal(sub, message.GetHeader(CaravanHeaders.FileRelativePath));
        Assert.Equal("hello.txt", message.GetHeader(CaravanHeaders.FileNameOnly));
        Assert.Equal(path, message.GetHeader(CaravanHeaders.FileAbsolutePath));
        Assert.Equal(false, message.GetHeader(CaravanHeaders.FileAbsolute));
        Assert.Equal(Path.Combine(relative, "sub"), message.GetHeader(CaravanHeaders.FileParent));
        Assert.Equal(Path.Combine(relative, "sub", "hello.txt"), message.GetHeader(CaravanHeaders.FilePath));
        Assert.Equal(5L, message.GetHeader(CaravanHeaders.FileLength));
        Assert.Equal(1_700_000_000_000L, message.GetHeader(CaravanHeaders.FileLastModified));
        Assert.Equal([".caravan/hello.txt"], FilesIn("in/sub"));
    }

    [Fact]
    public async Task Sets_absolute_headers_for_an_absolute_starting_directory()
    {
        var path = WriteFile("in/hello.txt", "Hello");
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From(From("in")).To("mock:result"));
        await FirstPollAsync();
        await mock.AssertIsSatisfiedAsync();

        var message = mock.ReceivedExchanges[0].Message;
        Assert.Equal(true, message.GetHeader(CaravanHeaders.FileAbsolute));
        Assert.Equal(path, message.GetHeader(CaravanHeaders.FilePath));
        Assert.Equal(PathOf("in"), message.GetHeader(CaravanHeaders.FileParent));
        Assert.Equal("hello.txt", message.GetHeader(CaravanHeaders.FileName));
        Assert.Same(message.Body, mock.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.FileExchangeFile));
    }

    [Fact]
    public async Task The_body_is_a_lazy_generic_file_convertible_to_stream_bytes_and_text()
    {
        WriteFile("in/data.txt", "content");
        object? body = null;
        string? text = null;
        byte[]? bytes = null;
        Type? streamType = null;
        FileInfo? info = null;
        await StartWithRoutes(b => b.From(From("in", "noop=true")).Process(e =>
        {
            body = e.Message.Body;
            text = e.Message.GetBody<string>();
            bytes = e.Message.GetBody<byte[]>();
            using var stream = e.Message.GetBody<Stream>()!;
            streamType = stream.GetType();
            info = e.Message.GetBody<FileInfo>();
        }));
        await FirstPollAsync();

        var file = Assert.IsType<GenericFile<FileInfo>>(body);
        Assert.Equal("GenericFile[data.txt]", file.ToString());
        Assert.Equal("content", text);
        Assert.Equal("content"u8.ToArray(), bytes);
        Assert.Equal(typeof(FileStream), streamType);
        Assert.Equal(PathOf("in", "data.txt"), info!.FullName);
    }

    [Fact]
    public async Task Charset_is_used_to_read_text()
    {
        File.WriteAllBytes(PathOf("latin.txt"), [0x63, 0x61, 0x66, 0xE9]);
        Directory.CreateDirectory(PathOf("in"));
        File.Move(PathOf("latin.txt"), PathOf("in", "latin.txt"));
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("café");
        await StartWithRoutes(b => b.From(From("in", "charset=ISO-8859-1")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal("ISO-8859-1", mock.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.CharsetName));
    }

    [Fact]
    public async Task Noop_leaves_the_file_and_is_idempotent_across_polls()
    {
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("A");
        await StartWithRoutes(b => b.From(From("in", "noop=true")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        await NextPollAsync();
        await NextPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal(["a.txt"], FilesIn("in"));
        Assert.True(Endpoint(From("in", "noop=true")).IsIdempotent);
    }

    [Fact]
    public async Task Noop_with_idempotent_false_reads_the_file_again_on_every_poll()
    {
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        mock.ExpectedMessageCount(3);
        await StartWithRoutes(b => b.From(From("in", "noop=true&idempotent=false")).To("mock:result"));
        await FirstPollAsync();
        await NextPollAsync();
        await NextPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Idempotent_key_expression_lets_a_changed_file_be_read_again()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WriteFile("in/a.txt", "v1", start);
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("v1", "v2");
        await StartWithRoutes(b => b.From(From("in", "noop=true&idempotentKey=${file:name}-${file:modified}")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        await NextPollAsync();
        WriteFile("in/a.txt", "v2", start.AddMinutes(1));
        await NextPollAsync();
        await NextPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Idempotent_repository_from_the_registry_is_used()
    {
        var repository = new MemoryIdempotentRepository();
        await repository.AddAsync(PathOf("in", "done.txt"));
        Context.Registry.Bind("repo", repository);
        WriteFile("in/done.txt", "old");
        WriteFile("in/new.txt", "new");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("new");
        await StartWithRoutes(b => b.From(From("in", "idempotent=true&idempotentRepository=#repo")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.True(await repository.ContainsAsync(PathOf("in", "new.txt")));
        Assert.Contains("done.txt", FilesIn("in"));
    }

    [Fact]
    public async Task Idempotent_with_move_does_not_take_a_file_with_the_same_name_again()
    {
        WriteFile("in/a.txt", "first");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("first");
        await StartWithRoutes(b => b.From(From("in", "idempotent=true")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        WriteFile("in/a.txt", "second");
        await NextPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt", "a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Delete_removes_the_file_after_processing()
    {
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From(From("in", "delete=true")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Empty(FilesIn("in"));
    }

    [Fact]
    public async Task Delete_and_move_together_are_rejected()
    {
        await Context.StartAsync();
        var e = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint(From("in", "delete=true&move=done")));
        Assert.Contains("You cannot set both delete=true and move options", e.Message);
    }

    [Fact]
    public async Task Move_to_a_plain_directory_is_relative_to_the_file()
    {
        WriteFile("in/sub/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in", "recursive=true&move=done")).To("mock:result"));
        await FirstPollAsync();

        Assert.Equal(["sub/done/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Move_with_an_expression_is_relative_to_the_starting_directory()
    {
        WriteFile("in/sub/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in", "recursive=true&move=backup/${date:now:yyyyMMdd}/${file:name}.bak")).To("mock:result"));
        await FirstPollAsync();

        Assert.Equal(["backup/20260101/sub/a.txt.bak"], FilesIn("in"));
    }

    [Fact]
    public async Task Move_to_an_absolute_directory()
    {
        WriteFile("in/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in", "move=" + PathOf("archive"))).To("mock:result"));
        await FirstPollAsync();

        Assert.Empty(FilesIn("in"));
        Assert.Equal(["a.txt"], FilesIn("archive"));
    }

    [Fact]
    public async Task Move_replaces_an_existing_file_at_the_target()
    {
        WriteFile("in/.caravan/a.txt", "old");
        WriteFile("in/a.txt", "new");
        await StartWithRoutes(b => b.From(From("in")).To("mock:result"));
        await FirstPollAsync();

        Assert.Equal("new", ReadFile("in/.caravan/a.txt"));
    }

    [Fact]
    public async Task PreMove_moves_the_file_before_processing_and_updates_the_headers()
    {
        WriteFile("in/a.txt", "A");
        string? seenPath = null;
        bool? existedAtOriginalPlace = null;
        await StartWithRoutes(b => b.From(From("in", "preMove=inprogress&move=../done")).Process(e =>
        {
            seenPath = e.Message.GetHeader<string>(CaravanHeaders.FileAbsolutePath);
            existedAtOriginalPlace = File.Exists(PathOf("in", "a.txt"));
            Assert.Equal("A", e.Message.GetBody<string>());
        }));
        await FirstPollAsync();

        Assert.Equal(PathOf("in", "inprogress", "a.txt"), seenPath);
        Assert.False(existedAtOriginalPlace);
        Assert.Equal(["done/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task MoveFailed_moves_a_failed_file()
    {
        WriteFile("in/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in", "moveFailed=error/${file:onlyname.noext}.failed")).Process(_ => throw new InvalidOperationException("boom")));
        await FirstPollAsync();

        Assert.Equal(["error/a.failed"], FilesIn("in"));
    }

    [Fact]
    public async Task A_failed_file_without_moveFailed_stays_and_is_retried_at_the_next_poll()
    {
        WriteFile("in/a.txt", "A");
        var attempts = 0;
        await StartWithRoutes(b => b.From(From("in")).Process(_ =>
        {
            if (++attempts == 1)
            {
                throw new InvalidOperationException("boom");
            }
        }));
        await FirstPollAsync();
        Assert.Equal(["a.txt"], FilesIn("in"));

        await NextPollAsync();
        Assert.Equal(2, attempts);
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task A_failure_handled_by_the_error_handler_commits_the_file()
    {
        WriteFile("in/a.txt", "A");
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From(From("in", "moveFailed=failed")).Process(_ => throw new InvalidOperationException("boom"));
        });
        await FirstPollAsync();

        Assert.Equal(1, Mock("dead").ReceivedCounter);
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Recursive_takes_the_files_of_sub_directories()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/x/b.txt", "B");
        WriteFile("in/x/y/c.txt", "C");
        var mock = Mock("result");
        mock.ExpectedBodiesReceivedInAnyOrder("A", "B", "C");
        await StartWithRoutes(b => b.From(From("in", "recursive=true&noop=true")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Without_recursive_sub_directories_are_ignored()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/x/b.txt", "B");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("A");
        await StartWithRoutes(b => b.From(From("in", "noop=true")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Theory]
    [InlineData("minDepth=2", "B,C")]
    [InlineData("maxDepth=1", "A")]
    [InlineData("minDepth=2&maxDepth=2", "B")]
    [InlineData("minDepth=3", "C")]
    public async Task Min_and_max_depth_limit_the_levels_taken(string options, string expected)
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/x/b.txt", "B");
        WriteFile("in/x/y/c.txt", "C");
        var mock = Mock("result");
        mock.ExpectedBodiesReceivedInAnyOrder(expected.Split(','));
        await StartWithRoutes(b => b.From(From("in", "recursive=true&noop=true&" + options)).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Theory]
    [InlineData("include=.*\\.txt", "a.txt,c.TXT,sub/e.txt")]
    [InlineData("exclude=.*\\.txt", "b.xml,d.tar.gz")]
    [InlineData("include=.*\\.txt&exclude=c.*", "a.txt,sub/e.txt")]
    [InlineData("includeExt=xml,gz", "b.xml,d.tar.gz")]
    [InlineData("includeExt=tar.gz", "d.tar.gz")]
    [InlineData("excludeExt=txt", "b.xml,d.tar.gz")]
    [InlineData("includeExt=txt,xml&excludeExt=xml", "a.txt,c.TXT,sub/e.txt")]
    [InlineData("antInclude=**/*.txt", "a.txt,sub/e.txt")]
    [InlineData("antInclude=sub/**", "sub/e.txt")]
    [InlineData("antInclude=*.txt,*.xml&antFilterCaseSensitive=false", "a.txt,b.xml,c.TXT")]
    [InlineData("antExclude=sub/**,*.gz", "a.txt,b.xml,c.TXT")]
    [InlineData("antInclude=**/*.txt&antExclude=**/e.*", "a.txt")]
    [InlineData("filterFile=${file:size} > 1", "d.tar.gz")]
    [InlineData("filterDirectory=${file:name} != 'sub'", "a.txt,b.xml,c.TXT,d.tar.gz")]
    [InlineData("fileName=b.xml", "b.xml")]
    public async Task Filters_choose_the_files(string options, string expected)
    {
        WriteFile("in/a.txt", "1");
        WriteFile("in/b.xml", "2");
        WriteFile("in/c.TXT", "3");
        WriteFile("in/d.tar.gz", "44");
        WriteFile("in/sub/e.txt", "5");
        var names = new List<string>();
        await StartWithRoutes(b => b.From(From("in", "noop=true&recursive=true&" + options))
            .Process(e => names.Add(e.Message.GetHeader<string>(CaravanHeaders.FileName)!.Replace('\\', '/'))));
        await FirstPollAsync();

        Assert.Equal(expected.Split(','), names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Hidden_files_and_lock_files_are_skipped_unless_included()
    {
        WriteFile("in/.hidden", "H");
        WriteFile("in/a.txt", "A");
        WriteFile("in/.dir/b.txt", "B");
        var names = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await StartWithRoutes(b =>
        {
            b.From(From("in", "noop=true&recursive=true")).Process(e => names.Enqueue("default:" + e.Message.GetHeader<string>(CaravanHeaders.FileNameOnly)));
            b.From(From("in", "noop=true&recursive=true&includeHiddenFiles=true&includeHiddenDirs=true&readLock=none"))
                .Process(e => names.Enqueue("hidden:" + e.Message.GetHeader<string>(CaravanHeaders.FileNameOnly)));
        });
        await FirstPollAsync(2);

        Assert.Equal(["default:a.txt", "hidden:.hidden", "hidden:a.txt", "hidden:b.txt"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Custom_filter_from_the_registry()
    {
        Context.Registry.Bind("myFilter", new NameStartsWithFilter("keep"));
        WriteFile("in/keep-1.txt", "1");
        WriteFile("in/drop-2.txt", "2");
        WriteFile("in/keepdir/x.txt", "3");
        WriteFile("in/other/keep-3.txt", "4");
        var mock = Mock("result");
        mock.ExpectedBodiesReceivedInAnyOrder("1", "4");
        await StartWithRoutes(b => b.From(From("in", "noop=true&recursive=true&filter=#myFilter")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Theory]
    [InlineData("sortBy=file:name", "B.txt,a.txt,c.txt")]
    [InlineData("sortBy=ignoreCase:file:name", "a.txt,B.txt,c.txt")]
    [InlineData("sortBy=reverse:file:name", "c.txt,a.txt,B.txt")]
    [InlineData("sortBy=reverse:ignoreCase:file:name", "c.txt,B.txt,a.txt")]
    [InlineData("sortBy=file:modified", "c.txt,a.txt,B.txt")]
    [InlineData("sortBy=reverse:file:modified", "B.txt,a.txt,c.txt")]
    [InlineData("sortBy=file:length;file:name", "B.txt,c.txt,a.txt")]
    [InlineData("sortBy=file:length;reverse:${file:name}", "c.txt,B.txt,a.txt")]
    public async Task SortBy_orders_the_files(string options, string expected)
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WriteFile("in/a.txt", "aaa", start.AddMinutes(2));
        WriteFile("in/B.txt", "b", start.AddMinutes(3));
        WriteFile("in/c.txt", "c", start.AddMinutes(1));
        var names = new List<string>();
        await StartWithRoutes(b => b.From(From("in", "noop=true&" + options)).Process(e => names.Add(e.Message.GetHeader<string>(CaravanHeaders.FileName)!)));
        await FirstPollAsync();

        Assert.Equal(expected.Split(','), names);
    }

    [Fact]
    public async Task Sorter_from_the_registry()
    {
        Context.Registry.Bind("bySize", Comparer<GenericFile<FileInfo>>.Create((x, y) => y.FileLength.CompareTo(x.FileLength)));
        WriteFile("in/a.txt", "a");
        WriteFile("in/b.txt", "bbb");
        WriteFile("in/c.txt", "cc");
        var names = new List<string>();
        await StartWithRoutes(b => b.From(From("in", "noop=true&sorter=#bySize")).Process(e => names.Add(e.Message.GetHeader<string>(CaravanHeaders.FileName)!)));
        await FirstPollAsync();

        Assert.Equal(["b.txt", "c.txt", "a.txt"], names);
    }

    [Fact]
    public async Task Shuffle_processes_every_file_once()
    {
        for (var i = 0; i < 10; i++)
        {
            WriteFile($"in/{i}.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var mock = Mock("result");
        mock.ExpectedMessageCount(10);
        mock.ExpectsNoDuplicates(Expr.Header(CaravanHeaders.FileName));
        await StartWithRoutes(b => b.From(From("in", "shuffle=true")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task MaxMessagesPerPoll_limits_each_poll_and_sets_the_batch_properties()
    {
        for (var i = 1; i <= 5; i++)
        {
            WriteFile($"in/{i}.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "maxMessagesPerPoll=2&sortBy=file:name")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        Assert.Equal(2, mock.ReceivedCounter);
        Assert.Equal(3, FilesIn("in").Count(f => !f.StartsWith('.')));

        await NextPollAsync();
        await NextPollAsync();
        Assert.Equal(["1", "2", "3", "4", "5"], mock.ReceivedExchanges.Select(e => e.Message.Body as string).Order());
        var first = mock.ReceivedExchanges[0];
        Assert.Equal(0, first.GetProperty(ExchangePropertyKeys.BatchIndex));
        Assert.Equal(2, first.GetProperty(ExchangePropertyKeys.BatchSize));
        Assert.Equal(false, first.GetProperty(ExchangePropertyKeys.BatchComplete));
        Assert.Equal(true, mock.ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.BatchComplete));
        Assert.Equal(1, mock.ReceivedExchanges[4].GetProperty(ExchangePropertyKeys.BatchSize));
    }

    [Fact]
    public async Task Non_eager_max_messages_sorts_all_files_before_limiting()
    {
        foreach (var name in (string[])["c", "a", "e", "b", "d"])
        {
            WriteFile($"in/{name}.txt", name);
        }

        var names = new List<string>();
        await StartWithRoutes(b => b.From(From("in", "noop=true&maxMessagesPerPoll=2&eagerMaxMessagesPerPoll=false&sortBy=reverse:file:name"))
            .Process(e => names.Add(e.Message.GetHeader<string>(CaravanHeaders.FileName)!)));
        await FirstPollAsync();

        Assert.Equal(["e.txt", "d.txt"], names);
        await NextPollAsync();
        Assert.Equal(["e.txt", "d.txt", "c.txt", "b.txt"], names);
    }

    [Fact]
    public async Task PreSort_lists_entries_by_name_before_the_eager_limit()
    {
        foreach (var name in (string[])["d", "b", "a", "c"])
        {
            WriteFile($"in/{name}.txt", name);
        }

        var names = new List<string>();
        await StartWithRoutes(b => b.From(From("in", "noop=true&preSort=true&maxMessagesPerPoll=2"))
            .Process(e => names.Add(e.Message.GetHeader<string>(CaravanHeaders.FileName)!)));
        await FirstPollAsync();

        Assert.Equal(["a.txt", "b.txt"], names);
    }

    [Fact]
    public async Task Batch_consumer_properties_complete_the_aggregator()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        WriteFile("in/c.txt", "C");
        var mock = Mock("aggregated");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From(From("in", "sortBy=file:name")).ConvertBodyTo(typeof(string))
            .Aggregate(Expr.Constant("all"), AggregationStrategies.String("+"), a => a.CompletionFromBatchConsumer(), then => then.To("mock:aggregated")));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal("A+B+C", mock.ReceivedExchanges[0].Message.Body);
        Assert.Equal("consumer", mock.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
    }

    [Fact]
    public async Task Done_file_with_a_dynamic_name_is_required_and_deleted()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        WriteFile("in/a.ready", string.Empty);
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("A");
        await StartWithRoutes(b => b.From(From("in", "doneFileName=${file:name.noext}.ready")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt", "b.txt"], FilesIn("in"));

        mock.Reset();
        mock.ExpectedBodiesReceived("B");
        WriteFile("in/b.ready", string.Empty);
        await NextPollAsync();
        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt", ".caravan/b.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Static_done_file_is_deleted_once_the_batch_is_complete()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        WriteFile("in/ready", string.Empty);
        bool? doneFileDuringFirst = null;
        await StartWithRoutes(b => b.From(From("in", "doneFileName=ready&sortBy=file:name")).Process(e =>
        {
            doneFileDuringFirst ??= Exists("in/ready");
        }));
        await FirstPollAsync();

        Assert.True(doneFileDuringFirst);
        Assert.Equal([".caravan/a.txt", ".caravan/b.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Done_file_names_with_other_functions_are_rejected()
    {
        await Context.StartAsync();
        var e = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint(From("in", "doneFileName=${file:onlyname}.done")));
        Assert.Contains("only ${file:name} and ${file:name.noext}", e.Message);
    }

    [Fact]
    public async Task Flatten_sets_the_file_name_without_directories()
    {
        WriteFile("in/sub/a.txt", "A");
        var mock = Mock("result");
        mock.ExpectedHeaderReceived(CaravanHeaders.FileName, "a.txt");
        await StartWithRoutes(b => b.From(From("in", "recursive=true&flatten=true&noop=true")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Missing_directory_is_created_by_default()
    {
        await StartWithRoutes(b => b.From(From("created/deep")).To("mock:result"));
        await FirstPollAsync();

        Assert.True(Directory.Exists(PathOf("created", "deep")));
    }

    [Fact]
    public async Task StartingDirectoryMustExist_fails_the_route_start_without_autoCreate()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From(From("missing", "autoCreate=false&startingDirectoryMustExist=true")).To("mock:result")));
        var e = await Assert.ThrowsAnyAsync<Exception>(() => Context.StartAsync());
        Assert.Contains("Starting directory does not exist", e.ToString());
    }

    [Fact]
    public async Task DirectoryMustExist_fails_polls_and_the_health_check_is_down()
    {
        await StartWithRoutes(b => b.From(From("missing", "autoCreate=false&directoryMustExist=true")).RouteId("r").To("mock:result"));
        await FirstPollAsync();

        var consumer = Assert.IsType<GenericFileConsumer<FileInfo>>(Context.GetRoute("r")!.Consumer);
        Assert.IsType<GenericFileOperationFailedException>(consumer.LastPollError);
        var result = await CheckAsync("consumer:r");
        Assert.Equal(Health.HealthState.Down, result.State);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public async Task Health_check_is_up_while_the_directory_exists_and_down_once_it_is_gone()
    {
        await StartWithRoutes(b => b.From(From("in")).RouteId("r").To("mock:result"));
        await FirstPollAsync();
        Assert.Equal(Health.HealthState.Up, (await CheckAsync("consumer:r")).State);

        Directory.Delete(PathOf("in"), recursive: true);
        Assert.Equal(Health.HealthState.Down, (await CheckAsync("consumer:r")).State);
    }

    [Fact]
    public async Task Each_file_is_processed_once_per_poll_and_files_in_progress_are_skipped()
    {
        WriteFile("in/a.txt", "A");
        var endpoint = Endpoint(From("in"));
        await endpoint.InProgressRepository.AddAsync(PathOf("in", "a.txt"));
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in")).To("mock:result"));
        await FirstPollAsync();
        Assert.Equal(0, mock.ReceivedCounter);

        await endpoint.InProgressRepository.RemoveAsync(PathOf("in", "a.txt"));
        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);
        Assert.False(await endpoint.InProgressRepository.ContainsAsync(PathOf("in", "a.txt")));
    }

    [Fact]
    public async Task A_file_deleted_after_listing_is_skipped()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "sortBy=file:name")).Process(e =>
        {
            // While a.txt is processed, b.txt (already listed) disappears.
            File.Delete(PathOf("in", "b.txt"));
        }).To("mock:result"));
        await FirstPollAsync();

        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
        Assert.False(await Endpoint(From("in", "sortBy=file:name")).InProgressRepository.ContainsAsync(PathOf("in", "b.txt")));
    }

    [Fact]
    public async Task Commit_happens_when_the_unit_of_work_completes_after_a_seda_hand_over()
    {
        WriteFile("in/a.txt", "A");
        var release = TestHelpers.NewSignal();
        var processing = TestHelpers.NewSignal();
        await StartWithRoutes(b =>
        {
            b.From(From("in")).To("seda:work");
            b.From("seda:work").Process(async _ =>
            {
                processing.TrySetResult();
                await release.Task;
            }).To("mock:result");
        });
        try
        {
            await FirstPollAsync();
            await processing.Task.Bounded();

            // The file route is done, but the exchange is still being worked on: the file is not committed yet (and still locked).
            Assert.Equal(["a.txt", "a.txt.caravanLock"], FilesIn("in"));
        }
        finally
        {
            release.TrySetResult();
        }

        await TestHelpers.WaitUntilAsync(() => FilesIn("in").SequenceEqual([".caravan/a.txt"]));
    }

    [Fact]
    public async Task Completion_failures_go_to_the_on_completion_exception_handler()
    {
        var handler = new RecordingExceptionHandler();
        Context.Registry.Bind("handler", handler);
        WriteFile("in/a.txt", "A");
        WriteFile("in/blocker", string.Empty);
        await StartWithRoutes(b => b.From(From("in", "include=a.txt&move=blocker/sub&onCompletionExceptionHandler=#handler")).To("mock:result"));
        await FirstPollAsync();

        var (message, exception) = Assert.Single(handler.Handled);
        Assert.Contains("Error during commit", message);
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task Stop_completes_the_current_file_and_leaves_the_rest()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        WriteFile("in/c.txt", "C");
        var release = TestHelpers.NewSignal();
        var processing = TestHelpers.NewSignal();
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "sortBy=file:name")).RouteId("r").Process(async _ =>
        {
            processing.TrySetResult();
            await release.Task;
        }).To("mock:result"));
        Task stop;
        try
        {
            await processing.Task.Bounded();
            stop = Context.StopRouteAsync("r");
        }
        finally
        {
            release.TrySetResult();
        }

        await stop.Bounded();

        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal([".caravan/a.txt", "b.txt", "c.txt"], FilesIn("in"));
        var endpoint = Endpoint(From("in", "sortBy=file:name"));
        Assert.False(await endpoint.InProgressRepository.ContainsAsync(PathOf("in", "b.txt")));
    }

    [Fact]
    public async Task Stop_with_complete_all_tasks_processes_the_rest_of_the_batch()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        WriteFile("in/c.txt", "C");
        var release = TestHelpers.NewSignal();
        var processing = TestHelpers.NewSignal();
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "sortBy=file:name")).RouteId("r").ShutdownRunningTask(ShutdownRunningTask.CompleteAllTasks)
            .Process(async _ =>
            {
                processing.TrySetResult();
                await release.Task;
            }).To("mock:result"));
        Task stop;
        try
        {
            await processing.Task.Bounded();
            stop = Context.StopRouteAsync("r");
        }
        finally
        {
            release.TrySetResult();
        }

        await stop.Bounded();

        Assert.Equal(3, mock.ReceivedCounter);
        Assert.Equal([".caravan/a.txt", ".caravan/b.txt", ".caravan/c.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Suspend_stops_polling_and_resume_continues()
    {
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in")).RouteId("r").To("mock:result"));
        await FirstPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);

        await Context.SuspendRouteAsync("r");
        WriteFile("in/b.txt", "B");
        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);

        await Context.ResumeRouteAsync("r");
        await NextPollAsync();
        Assert.Equal(2, mock.ReceivedCounter);
    }

    [Fact]
    public async Task Send_empty_message_when_idle()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From(From("in", "sendEmptyMessageWhenIdle=true")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Null(mock.ReceivedExchanges[0].Message.Body);
    }

    [Fact]
    public async Task Greedy_polls_again_at_once_while_files_are_found()
    {
        for (var i = 0; i < 3; i++)
        {
            WriteFile($"in/{i}.txt", "x");
        }

        var mock = Mock("result");
        mock.ExpectedMessageCount(3);
        await StartWithRoutes(b => b.From(From("in", "greedy=true&maxMessagesPerPoll=1")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Custom_process_strategy_from_the_registry()
    {
        var strategy = new RecordingStrategy();
        Context.Registry.Bind("strategy", strategy);
        WriteFile("in/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in", "processStrategy=#strategy")).To("mock:result"));
        await FirstPollAsync();

        Assert.Equal(["prepare", "begin:a.txt", "commit:a.txt"], strategy.Calls);
        Assert.Equal(["a.txt"], FilesIn("in"));
    }

    private async Task<Health.HealthCheckResult> CheckAsync(string id) =>
        Assert.Single(await Context.HealthChecks.CheckAsync(c => c.Id == id));

    private sealed class NameStartsWithFilter(string prefix) : IGenericFileFilter<FileInfo>
    {
        public bool Accept(GenericFile<FileInfo> file) => file.IsDirectory ? !file.FileNameOnly.StartsWith(prefix, StringComparison.Ordinal) : file.FileNameOnly.StartsWith(prefix, StringComparison.Ordinal);
    }

    private sealed class RecordingExceptionHandler : IExceptionHandler
    {
        public List<(string Message, Exception Exception)> Handled { get; } = [];

        public void HandleException(string message, Exchange? exchange, Exception exception)
        {
            lock (Handled)
            {
                Handled.Add((message, exception));
            }
        }
    }

    private sealed class RecordingStrategy : IGenericFileProcessStrategy<FileInfo>
    {
        public List<string> Calls { get; } = [];

        public ValueTask PrepareOnStartupAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint)
        {
            Calls.Add("prepare");
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> BeginAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint, Exchange exchange, GenericFile<FileInfo> file, CancellationToken cancellationToken)
        {
            Calls.Add("begin:" + file.FileName);
            return ValueTask.FromResult(true);
        }

        public ValueTask AbortAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint, Exchange exchange, GenericFile<FileInfo> file)
        {
            Calls.Add("abort:" + file.FileName);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint, Exchange exchange, GenericFile<FileInfo> file)
        {
            Calls.Add("commit:" + file.FileName);
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint, Exchange exchange, GenericFile<FileInfo> file)
        {
            Calls.Add("rollback:" + file.FileName);
            return ValueTask.CompletedTask;
        }
    }
}
