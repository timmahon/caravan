namespace Caravan.Dsl.Yaml.Tests;

public class FileComponentTests : YamlTestBase
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caravan-yaml-file-tests", Guid.NewGuid().ToString("N"));

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Yaml_route_consumes_and_writes_files()
    {
        var input = Path.Combine(_dir, "in");
        var output = Path.Combine(_dir, "out");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "a.csv"), "1,2");
        await File.WriteAllTextAsync(Path.Combine(input, "skip.txt"), "no");
        var dir = _dir.Replace('\\', '/');

        await StartAsync($$"""
            - route:
                id: files
                from:
                  uri: file:{{dir}}/in
                  parameters:
                    initialDelay: 0
                    include: ".*\\.csv"
                    move: done/${file:onlyname}
                  steps:
                    - setHeader:
                        name: CaravanFileName
                        simple: "${file:onlyname.noext}.out"
                    - to:
                        uri: file:{{dir}}/out
                        parameters:
                          tempPrefix: .tmp-
                          doneFileName: ${file:name}.done
                    - to: mock:written
            """);
        var mock = Mock("written");
        mock.ExpectedMessageCount(1);
        await mock.AssertIsSatisfiedAsync();

        Assert.Equal("1,2", await File.ReadAllTextAsync(Path.Combine(output, "a.out")));
        Assert.True(File.Exists(Path.Combine(output, "a.out.done")));
        Assert.Equal(Path.Combine(output, "a.out"), mock.ReceivedExchanges[0].Message.GetHeader(CaravanHeaders.FileNameProduced));

        // The file is moved once its exchange completed.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(Path.Combine(input, "done", "a.csv")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(Path.Combine(input, "done", "a.csv")));
        Assert.True(File.Exists(Path.Combine(input, "skip.txt")));
    }
}
