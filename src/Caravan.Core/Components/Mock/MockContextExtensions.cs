namespace Caravan.Components.Mock;

/// <summary>Context helpers for tests using <c>mock</c> endpoints.</summary>
public static class MockContextExtensions
{
    /// <summary>Gets (creating if needed) a mock endpoint, e.g. <c>context.GetMockEndpoint("mock:result")</c>.</summary>
    public static MockEndpoint GetMockEndpoint(this ICaravanContext context, string uri) =>
        context.GetEndpoint<MockEndpoint>(uri.StartsWith("mock:", StringComparison.Ordinal) ? uri : "mock:" + uri);
}
