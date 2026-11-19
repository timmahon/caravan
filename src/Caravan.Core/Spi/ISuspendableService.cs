namespace Caravan.Spi;

/// <summary>A service that can be suspended and resumed without being fully stopped.</summary>
public interface ISuspendableService : IService
{
    /// <summary>Suspends the service (e.g. a consumer stops taking new messages).</summary>
    Task SuspendAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes a suspended service.</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
}
