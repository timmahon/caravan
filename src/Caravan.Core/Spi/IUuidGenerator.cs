namespace Caravan.Spi;

/// <summary>
/// Generates the unique ids of exchanges and messages (Camel's <c>UuidGenerator</c>), set on <see cref="ICaravanContext.UuidGenerator"/>.
/// Built-ins in <c>Caravan.Impl</c>: <see cref="Impl.DefaultUuidGenerator"/> (the default), <see cref="Impl.RandomUuidGenerator"/>,
/// <see cref="Impl.ShortUuidGenerator"/>, <see cref="Impl.SimpleUuidGenerator"/> and <see cref="Impl.ClassicUuidGenerator"/>.
/// </summary>
/// <remarks>Implementations are called concurrently and must be thread-safe.</remarks>
public interface IUuidGenerator
{
    /// <summary>Generates a new id.</summary>
    /// <returns>The id, unique as far as the generator guarantees.</returns>
    string GenerateUuid();
}
