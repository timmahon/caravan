namespace Caravan.Management;

/// <summary>Management view of a component.</summary>
/// <param name="Scheme">The scheme it is registered under.</param>
/// <param name="Type">The component type name.</param>
/// <param name="Status">Its status, or null when it is only available (not created yet).</param>
public sealed record ComponentInfo(string Scheme, string? Type, Spi.ServiceStatus? Status);
