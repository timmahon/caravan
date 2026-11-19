using System.Text;
using Caravan.Builder;
using Caravan.Spi;
using Caravan.Streams;

namespace Caravan.Core.Tests;

public sealed class StreamCachingTests : ContextTestBase, IDisposable
{
    private readonly string _spoolDirectory = Path.Combine(Path.GetTempPath(), "caravan-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_spoolDirectory))
        {
            Directory.Delete(_spoolDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_stream_body_can_be_read_by_every_node()
    {
        var reads = new List<string>();
        await StartWithRoutes(b => b.From("direct:in")
            .Process(e => reads.Add(e.Message.GetBody<string>()!))
            .Process(e => reads.Add(new StreamReader((Stream)e.Message.Body!).ReadToEnd()))
            .Log("${body}")
            .ConvertBodyTo(typeof(string))
            .To("mock:out"));
        var mock = Mock("mock:out").ExpectedBodiesReceived("hello stream");

        await Template.SendBodyAsync("direct:in", NonSeekable("hello stream"));

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(["hello stream", "hello stream"], reads);
        Assert.Equal(1, Context.StreamCaching.CacheMemoryCounter);
    }

    [Fact]
    public async Task The_body_becomes_an_in_memory_stream_cache_and_the_source_is_disposed()
    {
        var source = NonSeekable("data");
        Type? bodyType = null;
        await StartWithRoutes(b => b.From("direct:in").Process(e => bodyType = e.Message.Body?.GetType()));

        await Template.SendBodyAsync("direct:in", source);

        Assert.Equal(typeof(ByteArrayStreamCache), bodyType);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task Stream_caching_can_be_disabled_for_the_context_or_a_route()
    {
        Context.StreamCaching.Enabled = false;
        var reads = new List<string>();
        await StartWithRoutes(b =>
        {
            b.From("direct:off").Process(e => reads.Add(e.Message.GetBody<string>()!)).Process(e => reads.Add(e.Message.GetBody<string>()!));
            b.From("direct:on").StreamCaching().Process(e => reads.Add(e.Message.GetBody<string>()!)).Process(e => reads.Add(e.Message.GetBody<string>()!));
        });

        await Template.SendBodyAsync("direct:off", NonSeekable("x"));
        await Template.SendBodyAsync("direct:on", NonSeekable("y"));

        Assert.Equal(["x", string.Empty, "y", "y"], reads);
    }

    [Fact]
    public async Task A_route_can_opt_out_while_the_context_caches()
    {
        Type? bodyType = null;
        await StartWithRoutes(b => b.From("direct:in").StreamCaching(false).Process(e => bodyType = e.Message.Body?.GetType()));

        await Template.SendBodyAsync("direct:in", NonSeekable("z"));

        Assert.Equal(typeof(NonSeekableStream), bodyType);
    }

    [Fact]
    public async Task Large_streams_spool_to_a_temporary_file_deleted_when_the_exchange_completes()
    {
        ConfigureSpooling(threshold: 16);
        string? path = null;
        bool existedDuringRoute = false;
        var payload = new string('x', 100);
        await StartWithRoutes(b => b.From("direct:in")
            .Process(e =>
            {
                var cache = Assert.IsType<FileStreamCache>(e.Message.Body);
                path = cache.FilePath;
                existedDuringRoute = File.Exists(path);
                Assert.False(cache.IsInMemory);
                Assert.Equal(100, cache.Length);
            })
            .Process(e => Assert.Equal(payload, e.Message.GetBody<string>()))
            .Split(Expr.Tokenize("x"), s => s.To("mock:parts"))
            .To("mock:out"));
        Mock("mock:parts").ExpectedMessageCount(100);
        Mock("mock:out").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:in", NonSeekable(payload));

        await MockEndpointAssert();
        Assert.True(existedDuringRoute);
        Assert.False(File.Exists(path));
        Assert.Equal(1, Context.StreamCaching.CacheSpoolCounter);
        Assert.Equal(100, Context.StreamCaching.CacheSpoolSize);
        Assert.Equal(1, Context.StreamCaching.SpoolFilesDeleted);
        Assert.StartsWith(_spoolDirectory, path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Small_streams_stay_in_memory_when_spooling_is_enabled()
    {
        ConfigureSpooling(threshold: 1024);
        Type? bodyType = null;
        await StartWithRoutes(b => b.From("direct:in").Process(e => bodyType = e.Message.Body?.GetType()));

        await Template.SendBodyAsync("direct:in", NonSeekable("small"));

        Assert.Equal(typeof(ByteArrayStreamCache), bodyType);
        Assert.Equal(0, Context.StreamCaching.CacheSpoolCounter);
    }

    [Fact]
    public async Task Spool_files_are_deleted_when_the_exchange_fails_and_the_directory_when_the_context_stops()
    {
        ConfigureSpooling(threshold: 4);
        string? path = null;
        await StartWithRoutes(b => b.From("direct:in")
            .Process(e => path = ((FileStreamCache)e.Message.Body!).FilePath)
            .Process(_ => throw new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:in", NonSeekable("0123456789")));

        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(_spoolDirectory));
        await Context.StopAsync();
        Assert.False(Directory.Exists(_spoolDirectory));
    }

    [Fact]
    public async Task Fan_out_branches_read_the_whole_cached_body()
    {
        ConfigureSpooling(threshold: 8);
        await StartWithRoutes(b => b.From("direct:in")
            .Multicast(m => m.To("direct:a").To("direct:b"), parallelProcessing: true)
            .ConvertBodyTo(typeof(string))
            .To("mock:out"));
        await Context.AddRoutesAsync(RouteBuilder.Create(b =>
        {
            b.From("direct:a").ConvertBodyTo(typeof(string)).To("mock:a");
            b.From("direct:b").ConvertBodyTo(typeof(string)).To("mock:b");
        }));
        var body = "branch body over the threshold";
        Mock("mock:a").ExpectedBodiesReceived(body);
        Mock("mock:b").ExpectedBodiesReceived(body);
        Mock("mock:out").ExpectedBodiesReceived(body);

        await Template.SendBodyAsync("direct:in", NonSeekable(body));

        await MockEndpointAssert();
    }

    [Fact]
    public async Task Seda_hand_over_keeps_the_spool_file_until_the_consumer_is_done()
    {
        ConfigureSpooling(threshold: 4);
        var done = TestHelpers.NewSignal<(string Path, bool Existed, string Body)>();
        await StartWithRoutes(b =>
        {
            b.From("direct:in").Process(_ => { }).To("seda:next");
            b.From("seda:next").Process(e =>
            {
                var cache = (FileStreamCache)e.Message.Body!;
                done.TrySetResult((cache.FilePath, File.Exists(cache.FilePath), e.Message.GetBody<string>()!));
            });
        });

        await Template.SendBodyAsync("direct:in", NonSeekable("spooled body"));
        var (path, existed, body) = await done.Task.Bounded();

        Assert.True(existed);
        Assert.Equal("spooled body", body);
        await TestHelpers.WaitUntilAsync(() => !File.Exists(path));
    }

    [Fact]
    public async Task Cached_output_stream_switches_to_a_file_beyond_the_threshold()
    {
        var strategy = new StreamCachingStrategy { SpoolEnabled = true, SpoolThreshold = 5, SpoolDirectory = _spoolDirectory };
        var exchange = new Exchange(Context);
        var small = new CachedOutputStream(strategy, exchange);
        small.Write("abc"u8);
        var memory = small.NewStreamCache();
        Assert.True(memory.IsInMemory);
        Assert.False(small.IsSpooled);

        var large = new CachedOutputStream(strategy, exchange);
        await large.WriteAsync(Encoding.UTF8.GetBytes("abc"));
        await large.WriteAsync(Encoding.UTF8.GetBytes("defgh"));
        Assert.True(large.IsSpooled);
        var file = (FileStreamCache)large.NewStreamCache();
        Assert.Equal(8, file.Length);
        Assert.Throws<ObjectDisposedException>(() => large.NewStreamCache());

        var copy = (Stream)file.Copy();
        Assert.Equal("abcdefgh", await new StreamReader(file).ReadToEndAsync());
        Assert.Equal("abcdefgh", await new StreamReader(copy).ReadToEndAsync());
        file.Reset();
        using var output = new MemoryStream();
        await file.WriteToAsync(output);
        Assert.Equal("abcdefgh", Encoding.UTF8.GetString(output.ToArray()));
        Assert.Equal(0, file.Position);

        var discarded = new CachedOutputStream(strategy, null);
        discarded.Write(new byte[10]);
        await discarded.DisposeAsync();
        Assert.Single(Directory.GetFiles(_spoolDirectory));
    }

    [Fact]
    public void Byte_array_stream_cache_copies_have_their_own_position_and_survive_dispose()
    {
        var cache = new ByteArrayStreamCache(Encoding.UTF8.GetBytes("abcdef"), 4);
        var copy = (ByteArrayStreamCache)cache.Copy();
        Assert.Equal(2, cache.Read(new byte[2], 0, 2));
        Assert.Equal(0, copy.Position);
        cache.Dispose();
        Assert.Equal("cd", Encoding.UTF8.GetString(cache.ToArray()[2..]));
        Assert.Equal(4, cache.Seek(0, SeekOrigin.End));
        Assert.Equal(0, cache.Read(new byte[2], 0, 2));
        Assert.Throws<NotSupportedException>(() => cache.WriteByte(1));
        Assert.Equal("abcd", Context.TypeConverter.Convert(typeof(string), null, cache));
        Assert.Equal("abcd", Context.TypeConverter.Convert(typeof(string), null, cache));
    }

    [Fact]
    public void Message_copies_get_independent_readers()
    {
        var exchange = new Exchange(Context);
        var cache = new ByteArrayStreamCache(Encoding.UTF8.GetBytes("abc"));
        exchange.Message.Body = cache;

        var copy = exchange.Copy();

        Assert.NotSame(cache, copy.Message.Body);
        Assert.IsAssignableFrom<IStreamCache>(copy.Message.Body);
    }

    private static NonSeekableStream NonSeekable(string text) => new(Encoding.UTF8.GetBytes(text));

    private void ConfigureSpooling(long threshold)
    {
        Context.StreamCaching.SpoolEnabled = true;
        Context.StreamCaching.SpoolThreshold = threshold;
        Context.StreamCaching.SpoolDirectory = _spoolDirectory;
    }

    private Task MockEndpointAssert() => Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);

    public sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
