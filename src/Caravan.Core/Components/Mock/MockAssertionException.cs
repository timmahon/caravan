namespace Caravan.Components.Mock;

/// <summary>Raised when a mock endpoint's expectations are not met.</summary>
public sealed class MockAssertionException(string message) : Exception(message);
