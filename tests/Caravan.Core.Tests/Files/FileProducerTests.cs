using System.Security.Cryptography;
using System.Text;
using Caravan.Builder;
using Caravan.Components.File;

namespace Caravan.Core.Tests;

public class FileProducerTests : FileTestBase
{
    [Fact]
    public async Task Writes_the_body_to_the_file_named_by_the_header()
    {
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out"), e =>
        {
            e.Message.Body = "Hello";
            e.Message.SetHeader(CaravanHeaders.FileName, "hello.txt");
        });

        Assert.Null(exchange.Exception);
        Assert.Equal("Hello", ReadFile("out/hello.txt"));
        Assert.Equal(PathOf("out", "hello.txt"), exchange.Message.GetHeader(CaravanHeaders.FileNameProduced));
        Assert.Equal("hello.txt", exchange.Message.GetHeader(CaravanHeaders.FileName));
    }

    [Fact]
    public async Task Without_a_name_the_message_id_is_used()
    {
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out"), e => e.Message.Body = "x");

        Assert.Equal([exchange.Message.MessageId], FilesIn("out"));
    }

    [Fact]
    public async Task FileName_option_is_a_simple_expression_and_creates_directories()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "fileName=${date:now:yyyyMMdd}/${header.id}.txt"), "x", "id", 42);

        Assert.Equal(["20260101/42.txt"], FilesIn("out"));
    }

    [Fact]
    public async Task AutoCreate_false_fails_when_the_directory_is_missing()
    {
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out", "autoCreate=false&fileName=a.txt"), e => e.Message.Body = "x");

        Assert.IsType<GenericFileOperationFailedException>(exchange.Exception);
        Assert.False(Directory.Exists(PathOf("out")));
    }

    [Fact]
    public async Task Overrule_header_is_used_once_and_removed()
    {
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out"), e =>
        {
            e.Message.Body = "x";
            e.Message.SetHeader(CaravanHeaders.FileName, "orig.txt");
            e.Message.SetHeader(CaravanHeaders.OverruleFileName, "over.txt");
        });

        Assert.Equal(["over.txt"], FilesIn("out"));
        Assert.False(exchange.Message.HasHeader(CaravanHeaders.OverruleFileName));
        Assert.Equal("orig.txt", exchange.Message.GetHeader(CaravanHeaders.FileName));
    }

    [Fact]
    public async Task Flatten_drops_the_directories_of_the_name()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "flatten=true"), "x", CaravanHeaders.FileName, "sub/dir/a.txt");

        Assert.Equal(["a.txt"], FilesIn("out"));
    }

    [Fact]
    public async Task FileExist_override_replaces_the_file()
    {
        WriteFile("out/a.txt", "old content");
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out"), "new", CaravanHeaders.FileName, "a.txt");

        Assert.Equal("new", ReadFile("out/a.txt"));
    }

    [Fact]
    public async Task FileExist_append_appends_with_append_chars()
    {
        await Context.StartAsync();
        var uri = To("out", "fileExist=Append&appendChars=\\n&fileName=log.txt");
        await Template.SendBodyAsync(uri, "one");
        await Template.SendBodyAsync(uri, "two");

        Assert.Equal("one\ntwo\n", ReadFile("out/log.txt"));
    }

    [Fact]
    public async Task Concurrent_appends_to_the_same_file_are_serialized()
    {
        await Context.StartAsync();
        var uri = To("out", "fileExist=Append&appendChars=\\n&fileName=log.txt&forceWrites=false");
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Template.SendBodyAsync(uri, new string((char)('a' + (i % 26)), 100))));

        var lines = ReadFile("out/log.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(50, lines.Length);
        Assert.All(lines, l => Assert.True(l.Length == 100 && l.All(c => c == l[0]), l));
    }

    [Fact]
    public async Task FileExist_fail_fails_when_the_file_exists()
    {
        WriteFile("out/a.txt", "old");
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out", "fileExist=Fail&fileName=a.txt"), e => e.Message.Body = "new");

        var error = Assert.IsType<GenericFileOperationFailedException>(exchange.Exception);
        Assert.Contains("File already exist", error.Message);
        Assert.Equal("old", ReadFile("out/a.txt"));
    }

    [Fact]
    public async Task FileExist_ignore_keeps_the_existing_file()
    {
        WriteFile("out/a.txt", "old");
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out", "fileExist=Ignore&fileName=a.txt"), e => e.Message.Body = "new");

        Assert.Null(exchange.Exception);
        Assert.Equal("old", ReadFile("out/a.txt"));
        Assert.False(exchange.Message.HasHeader(CaravanHeaders.FileNameProduced));
    }

    [Theory]
    [InlineData("backup", "backup/a.txt")]
    [InlineData("${file:name}.bak", "a.txt.bak")]
    [InlineData("${file:parent}/old/${file:onlyname.noext}-1.txt", "old/a-1.txt")]
    public async Task FileExist_move_moves_the_existing_file_first(string moveExisting, string movedTo)
    {
        WriteFile("out/a.txt", "old");
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "fileExist=Move&moveExisting=" + Uri.EscapeDataString(moveExisting)), "new", CaravanHeaders.FileName, "a.txt");

        Assert.Equal("new", ReadFile("out/a.txt"));
        Assert.Equal("old", ReadFile("out/" + movedTo));
    }

    [Fact]
    public async Task FileExist_move_replaces_an_earlier_backup()
    {
        WriteFile("out/a.txt", "second");
        WriteFile("out/backup/a.txt", "first");
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "fileExist=Move&moveExisting=backup"), "third", CaravanHeaders.FileName, "a.txt");

        Assert.Equal("second", ReadFile("out/backup/a.txt"));
        Assert.Equal("third", ReadFile("out/a.txt"));
    }

    [Theory]
    [InlineData("fileExist=Move", "You must configure the moveExisting option when fileExist=Move")]
    [InlineData("fileExist=Append&tempPrefix=tmp-", "You cannot set both fileExist=Append and tempPrefix/tempFileName options")]
    [InlineData("chmod=999", "Invalid permissions '999'")]
    public async Task Invalid_producer_options_are_rejected(string options, string message)
    {
        await Context.StartAsync();
        var e = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint(To("out", options)));
        Assert.Contains(message, e.ToString());
    }

    [Fact]
    public async Task TempPrefix_writes_a_temporary_file_then_renames_it()
    {
        await Context.StartAsync();
        var target = PathOf("out", "a.txt");
        var temp = PathOf("out", "tmp-a.txt");
        var body = new ObservingStream(64 * 1024, () => (File.Exists(temp), File.Exists(target)));
        await Template.SendBodyAndHeaderAsync(To("out", "tempPrefix=tmp-"), body, CaravanHeaders.FileName, "a.txt");

        Assert.Equal((true, false), body.Observed);
        Assert.Equal(["a.txt"], FilesIn("out"));
        Assert.Equal(64 * 1024, new FileInfo(target).Length);
    }

    [Fact]
    public async Task TempFileName_is_an_expression_on_the_target_name()
    {
        await Context.StartAsync();
        var temp = PathOf("out", "sub", "a.inprogress");
        var body = new ObservingStream(1024, () => (File.Exists(temp), false));
        await Template.SendBodyAndHeaderAsync(To("out", "tempFileName=${file:onlyname.noext}.inprogress"), body, CaravanHeaders.FileName, "sub/a.txt");

        Assert.True(body.Observed.Item1);
        Assert.Equal(["sub/a.txt"], FilesIn("out"));
    }

    [Fact]
    public async Task EagerDeleteTargetFile_false_keeps_the_old_file_until_the_rename()
    {
        WriteFile("out/a.txt", "old");
        await Context.StartAsync();
        var body = new ObservingStream(1024, () => (File.Exists(PathOf("out", "a.txt")), false));
        await Template.SendBodyAndHeaderAsync(To("out", "tempPrefix=tmp-&eagerDeleteTargetFile=false"), body, CaravanHeaders.FileName, "a.txt");

        Assert.True(body.Observed.Item1);
        Assert.Equal(1024, new FileInfo(PathOf("out", "a.txt")).Length);

        var eager = new ObservingStream(1024, () => (File.Exists(PathOf("out", "a.txt")), false));
        await Template.SendBodyAndHeaderAsync(To("out", "tempPrefix=tmp-"), eager, CaravanHeaders.FileName, "a.txt");
        Assert.False(eager.Observed.Item1);
    }

    [Fact]
    public async Task TryRename_fails_when_the_target_exists()
    {
        await Context.StartAsync();
        var uri = To("out", "fileExist=TryRename&tempPrefix=tmp-&fileName=a.txt");
        await Template.SendBodyAsync(uri, "first");
        var exchange = await Template.SendAsync(uri, e => e.Message.Body = "second");

        Assert.IsType<GenericFileOperationFailedException>(exchange.Exception);
        Assert.Equal("first", ReadFile("out/a.txt"));
    }

    [Fact]
    public async Task Done_file_is_written_after_the_file()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "doneFileName=${file:name}.done"), "x", CaravanHeaders.FileName, "a.txt");
        await Template.SendBodyAndHeaderAsync(To("out", "doneFileName=${file:name.noext}.ready"), "y", CaravanHeaders.FileName, "sub/b.txt");

        Assert.Equal(["a.txt", "a.txt.done", "sub/b.ready", "sub/b.txt"], FilesIn("out"));
        Assert.Equal(0, new FileInfo(PathOf("out", "a.txt.done")).Length);
    }

    [Fact]
    public async Task Done_files_written_by_a_producer_release_files_to_a_consumer()
    {
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("payload");
        await StartWithRoutes(b => b.From(From("exchange", "doneFileName=${file:name}.done")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();
        Assert.Equal(0, mock.ReceivedCounter);

        await Template.SendBodyAndHeaderAsync(To("exchange", "doneFileName=${file:name}.done"), "payload", CaravanHeaders.FileName, "a.txt");
        await NextPollAsync();
        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt"], FilesIn("exchange"));
    }

    [Fact]
    public async Task KeepLastModified_sets_the_modification_time_from_the_header()
    {
        await Context.StartAsync();
        var modified = new DateTimeOffset(2020, 5, 6, 7, 8, 9, TimeSpan.Zero);
        await Template.SendBodyAndHeadersAsync(To("out", "keepLastModified=true"), "x", new Dictionary<string, object?>
        {
            [CaravanHeaders.FileName] = "a.txt",
            [CaravanHeaders.FileLastModified] = modified.ToUnixTimeMilliseconds(),
        });

        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(PathOf("out", "a.txt")));
    }

    [Fact]
    public async Task Chmod_sets_the_permissions_of_files_and_created_directories()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permissions only
        }

        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "chmod=640&chmodDirectory=750"), "x", CaravanHeaders.FileName, "sub/a.txt");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(PathOf("out", "sub", "a.txt")));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute,
            File.GetUnixFileMode(PathOf("out", "sub")));
    }

    [Fact]
    public async Task Null_body_fails_unless_allowed()
    {
        await Context.StartAsync();
        var failed = await Template.SendAsync(To("out", "fileName=a.txt"), e => e.Message.Body = null);
        Assert.Contains("Cannot write null body", failed.Exception!.Message);

        var allowed = await Template.SendAsync(To("out", "fileName=a.txt&allowNullBody=true"), e => e.Message.Body = null);
        Assert.Null(allowed.Exception);
        Assert.Equal(0, new FileInfo(PathOf("out", "a.txt")).Length);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("/etc/caravan-escape.txt")]
    public async Task Jail_rejects_names_outside_the_starting_directory(string name)
    {
        await Context.StartAsync();
        var exchange = await Template.SendAsync(To("out"), e =>
        {
            e.Message.Body = "x";
            e.Message.SetHeader(CaravanHeaders.FileName, name);
        });

        var error = Assert.IsType<ArgumentException>(exchange.Exception);
        Assert.Contains("jailed to the starting directory", error.Message);
        Assert.False(Exists("escape.txt"));
    }

    [Fact]
    public async Task Jail_can_be_disabled()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "jailStartingDirectory=false"), "x", CaravanHeaders.FileName, "../outside.txt");

        Assert.Equal("x", ReadFile("outside.txt"));
    }

    [Fact]
    public async Task Charset_encodes_text_bodies()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out", "charset=ISO-8859-1"), "café", CaravanHeaders.FileName, "latin.txt");
        await Template.SendBodyAndHeaderAsync(To("out"), "café", CaravanHeaders.FileName, "utf8.txt");

        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, File.ReadAllBytes(PathOf("out", "latin.txt")));
        Assert.Equal(Encoding.UTF8.GetBytes("café"), File.ReadAllBytes(PathOf("out", "utf8.txt")));
    }

    [Fact]
    public async Task A_consumed_file_is_transcoded_between_charsets()
    {
        Directory.CreateDirectory(PathOf("in"));
        File.WriteAllBytes(PathOf("in", "a.txt"), [0x63, 0x61, 0x66, 0xE9]);
        await StartWithRoutes(b => b.From(From("in", "charset=ISO-8859-1")).To(To("out", "charset=UTF-8")));
        await FirstPollAsync();

        Assert.Equal(Encoding.UTF8.GetBytes("café"), File.ReadAllBytes(PathOf("out", "a.txt")));
    }

    [Fact]
    public async Task Stream_bodies_are_written_while_they_are_read()
    {
        await Context.StartAsync();
        var target = PathOf("out", "big.bin");
        const int size = 8 * 1024 * 1024;
        var body = new ObservingStream(size, () => (false, false), () => File.Exists(target) ? new FileInfo(target).Length : 0, observeAt: 6 * 1024 * 1024);
        await Template.SendBodyAndHeaderAsync(To("out", "forceWrites=false"), body, CaravanHeaders.FileName, "big.bin");

        Assert.Equal(size, new FileInfo(target).Length);

        // When 6 MB were read, megabytes were already on disk: the body is never buffered whole.
        Assert.InRange(body.ObservedLength, 1024 * 1024, 6 * 1024 * 1024);
    }

    [Fact]
    public async Task A_large_consumed_file_is_copied_by_streaming()
    {
        var data = new byte[12 * 1024 * 1024];
        new Random(42).NextBytes(data);
        Directory.CreateDirectory(PathOf("in", "sub"));
        File.WriteAllBytes(PathOf("in", "sub", "big.bin"), data);
        object? bodyAtProducer = null;
        await StartWithRoutes(b => b.From(From("in", "recursive=true")).Process(e => bodyAtProducer = e.Message.Body).To(To("out")));
        await FirstPollAsync();

        Assert.IsType<GenericFile<FileInfo>>(bodyAtProducer);
        Assert.Equal(SHA256.HashData(data), SHA256.HashData(File.ReadAllBytes(PathOf("out", "sub", "big.bin"))));
        Assert.Equal(["sub/.caravan/big.bin"], FilesIn("in"));
    }

    [Fact]
    public async Task Byte_array_and_other_bodies_are_written()
    {
        await Context.StartAsync();
        await Template.SendBodyAndHeaderAsync(To("out"), new byte[] { 1, 2, 3 }, CaravanHeaders.FileName, "bytes.bin");
        await Template.SendBodyAndHeaderAsync(To("out"), 42, CaravanHeaders.FileName, "number.txt");

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(PathOf("out", "bytes.bin")));
        Assert.Equal("42", ReadFile("out/number.txt"));
    }

    /// <summary>A stream of <c>size</c> bytes that records a state once, when <c>observeAt</c> bytes were read.</summary>
    private sealed class ObservingStream(long size, Func<(bool, bool)> observe, Func<long>? observeLength = null, long observeAt = 1) : Stream
    {
        private long _position;
        private bool _observed;

        public (bool, bool) Observed { get; private set; }

        public long ObservedLength { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => size;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_observed && _position >= observeAt)
            {
                _observed = true;
                Observed = observe();
                ObservedLength = observeLength?.Invoke() ?? 0;
            }

            var n = (int)Math.Min(count, size - _position);
            buffer.AsSpan(offset, n).Fill((byte)'x');
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
