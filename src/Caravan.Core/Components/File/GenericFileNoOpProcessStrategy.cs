namespace Caravan.Components.File;

/// <summary>The strategy of <c>noop=true</c> (Camel's <c>GenericFileNoOpProcessStrategy</c>): the file is left where it is, only the read lock applies.</summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class GenericFileNoOpProcessStrategy<T> : GenericFileProcessStrategySupport<T>;
