using System.Text.Json.Nodes;
using Caravan.Dsl.Yaml.Catalog;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml.Tests;

public class SchemaTests
{
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Caravan.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found");
    }

    private static string SchemaPath => Path.Combine(RepositoryRoot(), "schema", YamlDslSchema.FileName);

    /// <summary>Set CARAVAN_UPDATE_SCHEMA=1 to rewrite the committed schema after changing the catalog.</summary>
    [Fact]
    public void Committed_schema_is_up_to_date()
    {
        var generated = YamlDslSchema.Generate();
        if (Environment.GetEnvironmentVariable("CARAVAN_UPDATE_SCHEMA") == "1")
        {
            File.WriteAllText(SchemaPath, generated);
        }

        Assert.True(File.Exists(SchemaPath), $"{SchemaPath} is missing: run the tests with CARAVAN_UPDATE_SCHEMA=1");
        Assert.True(
            File.ReadAllText(SchemaPath).ReplaceLineEndings("\n") == generated,
            $"{SchemaPath} is out of date: run the tests with CARAVAN_UPDATE_SCHEMA=1 and commit the result");
    }

    [Fact]
    public void Schema_lists_every_step_and_its_options()
    {
        var schema = JsonNode.Parse(YamlDslSchema.Generate())!.AsObject();
        var definitions = schema["definitions"]!.AsObject();
        foreach (var (name, spec) in StepCatalog.Steps)
        {
            var step = definitions["step." + name];
            Assert.NotNull(step);
            var text = step.ToJsonString();
            foreach (var option in spec.Options.Where(o => o.Unsupported is null))
            {
                Assert.Contains($"\"{option.Name}\"", text);
            }
        }

        Assert.Equal(StepCatalog.Steps.Count, definitions["step"]!["properties"]!.AsObject().Count);
    }

    public static TheoryData<string> Samples() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.yaml").Select(Path.GetFileName).OfType<string>()];

    [Theory]
    [MemberData(nameof(Samples))]
    public void Samples_validate_against_the_schema(string sample)
    {
        var validator = new MiniSchemaValidator(JsonNode.Parse(YamlDslSchema.Generate())!.AsObject());
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", sample))));

        var errors = validator.Validate(MiniSchemaValidator.ToJson(yaml.Documents[0].RootNode));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("- from: { uri: direct:a, steps: [ { setBdy: { constant: x } } ] }")]
    [InlineData("- from: { uri: direct:a, steps: [ { setBody: { constant: x, foo: 1 } } ] }")]
    [InlineData("- from: { steps: [] }")]
    [InlineData("- from: { uri: direct:a, steps: [ { setHeader: { constant: x } } ] }")]
    [InlineData("- from: { uri: direct:a, steps: [ { to: { uri: mock:a, pattern: Sideways } } ] }")]
    [InlineData("- nope: {}")]
    public void Invalid_yaml_does_not_validate(string text)
    {
        var validator = new MiniSchemaValidator(JsonNode.Parse(YamlDslSchema.Generate())!.AsObject());
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));

        Assert.NotEmpty(validator.Validate(MiniSchemaValidator.ToJson(yaml.Documents[0].RootNode)));
    }
}
