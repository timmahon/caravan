using Caravan.Builder;
using Caravan.Components.File;
using Caravan.Idempotent;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

public class FileReadLockTests : FileTestBase
{
    [Fact]
    public async Task Marker_file_exists_while_the_file_is_processed_and_is_deleted_afterwards()
    {
        WriteFile("in/a.txt", "A");
        bool? lockedDuringProcessing = null;
        await StartWithRoutes(b => b.From(From("in")).Process(e =>
        {
            lockedDuringProcessing = Exists("in/a.txt" + MarkerFileExclusiveReadLockStrategy.LockFileSuffix);
            Assert.Equal(PathOf("in", "a.txt.caravanLock"), e.GetProperty(ExchangePropertyKeys.FileLockFileName));
        }));
        await FirstPollAsync();

        Assert.True(lockedDuringProcessing);
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Marker_file_is_deleted_when_the_exchange_fails()
    {
        WriteFile("in/a.txt", "A");
        await StartWithRoutes(b => b.From(From("in")).Process(_ => throw new InvalidOperationException("boom")));
        await FirstPollAsync();

        Assert.Equal(["a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task A_file_locked_by_another_consumer_is_skipped_until_the_lock_is_gone()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/a.txt.caravanLock", string.Empty);
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "readLockDeleteOrphanLockFiles=false")).To("mock:result"));
        await FirstPollAsync();
        await NextPollAsync();
        Assert.Equal(0, mock.ReceivedCounter);
        Assert.Equal(["a.txt", "a.txt.caravanLock"], FilesIn("in"));
        Assert.False(await Endpoint(From("in", "readLockDeleteOrphanLockFiles=false")).InProgressRepository.ContainsAsync(PathOf("in", "a.txt")));

        File.Delete(PathOf("in", "a.txt.caravanLock"));
        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);
    }

    [Fact]
    public async Task Orphan_lock_files_are_deleted_before_the_first_poll()
    {
        WriteFile("in/a.txt", "A");
        WriteFile("in/a.txt.caravanLock", string.Empty);
        WriteFile("in/sub/b.txt", "B");
        WriteFile("in/sub/b.txt.caravanLock", string.Empty);
        WriteFile("in/gone.txt.caravanLock", string.Empty);
        var mock = Mock("result");
        mock.ExpectedMessageCount(2);
        await StartWithRoutes(b => b.From(From("in", "recursive=true")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt", "sub/.caravan/b.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task ReadLock_none_creates_no_marker_file()
    {
        WriteFile("in/a.txt", "A");
        string[]? during = null;
        await StartWithRoutes(b => b.From(From("in", "readLock=none")).Process(_ => during = FilesIn("in")));
        await FirstPollAsync();

        Assert.Equal(["a.txt"], during!);
        Assert.Null(Endpoint(From("in", "readLock=none")).GenericFileProcessStrategy is GenericFileProcessStrategySupport<FileInfo> s ? s.ExclusiveReadLockStrategy : null);
    }

    [Fact]
    public async Task Changed_waits_until_the_file_stops_changing()
    {
        var path = WriteFile("in/a.txt", "part1");
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "readLock=changed&readLockCheckInterval=1000&readLockTimeout=10000")).ConvertBodyTo(typeof(string)).To("mock:result"));

        // First check: no previous state yet, so the lock waits one interval.
        await Time.WaitForTimersAsync(1);
        File.AppendAllText(path, "-part2");
        await NextPollAsync();
        Assert.Equal(0, mock.ReceivedCounter);

        // Unchanged since the last check: taken.
        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal("part1-part2", mock.ReceivedExchanges[0].Message.Body);
        Assert.Equal(11L, mock.ReceivedExchanges[0].Message.GetHeader(CaravanHeaders.FileLength));
    }

    [Fact]
    public async Task Changed_skips_a_file_below_the_minimum_length_after_the_timeout()
    {
        var path = WriteFile("in/a.txt", string.Empty);
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "readLock=changed&readLockCheckInterval=1000&readLockTimeout=2000")).To("mock:result"));
        await Time.WaitForTimersAsync(1);
        await NextPollAsync();
        await NextPollAsync();

        // Timed out: the file is left for the next poll, without lock.
        await NextPollAsync();
        Assert.Equal(0, mock.ReceivedCounter);
        Assert.Equal(["a.txt"], FilesIn("in"));

        File.WriteAllText(path, "now");
        await NextPollAsync();
        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);
    }

    [Fact]
    public async Task Changed_with_min_age_takes_an_old_file_at_once()
    {
        WriteFile("in/old.txt", "old", Time.GetUtcNow().AddMinutes(-2));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From(From("in", "readLock=changed&readLockMinAge=60000")).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Changed_with_min_age_waits_until_the_file_is_old_enough()
    {
        WriteFile("in/new.txt", "new", Time.GetUtcNow());
        var mock = Mock("result");
        await StartWithRoutes(b => b.From(From("in", "readLock=changed&readLockMinAge=60000&readLockCheckInterval=30000&readLockTimeout=600000")).To("mock:result"));
        await Time.WaitForTimersAsync(1);
        await Time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(30));
        await Time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, mock.ReceivedCounter);

        await Time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, mock.ReceivedCounter);
    }

    [Fact]
    public async Task FileLock_skips_a_file_open_for_writing_and_reads_through_its_lock()
    {
        var path = WriteFile("in/a.txt", "content");
        var mock = Mock("result");
        bool? othersLockedOut = null;
        var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        try
        {
            await StartWithRoutes(b => b.From(From("in", "readLock=fileLock&readLockCheckInterval=1000&readLockTimeout=2000")).Process(e =>
            {
                try
                {
                    using var other = File.OpenRead(path);
                    othersLockedOut = false;
                }
                catch (IOException)
                {
                    othersLockedOut = true;
                }
            }).ConvertBodyTo(typeof(string)).To("mock:result"));
            await Time.WaitForTimersAsync(1);
            await NextPollAsync();
            await NextPollAsync();

            // Timed out after 3 attempts: skipped, the marker file is gone.
            await NextPollAsync();
            Assert.Equal(0, mock.ReceivedCounter);
            Assert.Equal(["a.txt"], FilesIn("in"));
        }
        finally
        {
            await writer.DisposeAsync();
        }

        await NextPollAsync();
        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal("content", mock.ReceivedExchanges[0].Message.Body);
        Assert.True(othersLockedOut);
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Rename_read_lock_takes_a_file_that_can_be_renamed()
    {
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("A");
        await StartWithRoutes(b => b.From(From("in", "readLock=rename")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal([".caravan/a.txt"], FilesIn("in"));
    }

    [Fact]
    public async Task Idempotent_read_lock_lets_only_one_consumer_take_a_file()
    {
        var repository = new MemoryIdempotentRepository();
        Context.Registry.Bind("repo", repository);
        WriteFile("in/a.txt", "A");
        var mock = Mock("result");
        await StartWithRoutes(b =>
        {
            b.From(From("in", "readLock=idempotent&idempotentRepository=#repo&noop=true")).To("mock:result");
            b.From(From("in", "readLock=idempotent&idempotentRepository=#repo&noop=true&sortBy=file:name")).To("mock:result");
        });
        await FirstPollAsync(2);
        await Time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, mock.ReceivedCounter);
        Assert.True(await repository.ContainsAsync(PathOf("in", "a.txt")));
    }

    [Fact]
    public async Task Idempotent_read_lock_releases_the_key_on_rollback()
    {
        var repository = new MemoryIdempotentRepository();
        Context.Registry.Bind("repo", repository);
        WriteFile("in/a.txt", "A");
        var attempts = 0;
        await StartWithRoutes(b => b.From(From("in", "readLock=idempotent&idempotentRepository=#repo")).Process(_ =>
        {
            if (++attempts == 1)
            {
                throw new InvalidOperationException("boom");
            }
        }));
        await FirstPollAsync();
        Assert.False(await repository.ContainsAsync(PathOf("in", "a.txt")));

        await NextPollAsync();
        Assert.Equal(2, attempts);
        Assert.True(await repository.ContainsAsync(PathOf("in", "a.txt")));
    }

    [Fact]
    public async Task Read_lock_options_are_bound()
    {
        await Context.StartAsync();
        var endpoint = Endpoint(From("in", "readLock=changed&readLockLoggingLevel=WARN&readLockMarkerFile=false&readLockMinLength=0&readLockMinAge=5s"));
        Assert.Equal(LogLevel.Warning, endpoint.ReadLockLoggingLevel);
        var strategy = Assert.IsType<GenericFileRenameProcessStrategy<FileInfo>>(endpoint.GenericFileProcessStrategy);
        var changed = Assert.IsType<FileChangedExclusiveReadLockStrategy>(strategy.ExclusiveReadLockStrategy);
        Assert.Equal(0, changed.MinLength);
        Assert.Equal(TimeSpan.FromSeconds(5), changed.MinAge);

        var wrapped = Endpoint(From("in", "readLock=fileLock")).GenericFileProcessStrategy as GenericFileProcessStrategySupport<FileInfo>;
        var marker = Assert.IsType<MarkerFileExclusiveReadLockStrategy>(wrapped!.ExclusiveReadLockStrategy);
        Assert.IsType<FileLockExclusiveReadLockStrategy>(marker.Inner);
    }

    [Fact]
    public async Task Custom_read_lock_from_the_registry()
    {
        var calls = new List<string>();
        Context.Registry.Bind("lock", new OnlyNamedLock("b.txt", calls));
        WriteFile("in/a.txt", "A");
        WriteFile("in/b.txt", "B");
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("B");
        await StartWithRoutes(b => b.From(From("in", "exclusiveReadLockStrategy=#lock&sortBy=file:name")).ConvertBodyTo(typeof(string)).To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(["prepare", "acquire:a.txt", "abort:a.txt", "acquire:b.txt", "commit:b.txt"], calls);
        Assert.Equal([".caravan/b.txt", "a.txt"], FilesIn("in"));
    }

    [Theory]
    [InlineData("readLock=bogus", "Unknown readLock 'bogus'")]
    [InlineData("readLock=changed&readLockTimeout=1000&readLockCheckInterval=1000", "readLockTimeout must be higher than readLockCheckInterval")]
    public async Task Invalid_read_lock_options_are_rejected(string options, string message)
    {
        await Context.StartAsync();
        var e = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint(From("in", options)));
        Assert.Contains(message, e.Message);
    }

    private sealed class OnlyNamedLock(string name, List<string> calls) : IGenericFileExclusiveReadLockStrategy<FileInfo>
    {
        public ValueTask PrepareOnStartupAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint)
        {
            calls.Add("prepare");
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> AcquireExclusiveReadLockAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange, CancellationToken cancellationToken)
        {
            calls.Add("acquire:" + file.FileName);
            return ValueTask.FromResult(file.FileName == name);
        }

        public ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
        {
            calls.Add("abort:" + file.FileName);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
        {
            calls.Add("rollback:" + file.FileName);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
        {
            calls.Add("commit:" + file.FileName);
            return ValueTask.CompletedTask;
        }
    }
}
