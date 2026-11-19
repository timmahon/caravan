namespace Caravan.Core.Tests.Languages;

/// <summary>Serializes tests that change or observe the Simple language's global state (custom functions, caches).</summary>
[CollectionDefinition(Name)]
public sealed class SimpleGlobalStateCollection
{
    public const string Name = "Simple global state";
}
