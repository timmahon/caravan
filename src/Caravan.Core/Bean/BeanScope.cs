namespace Caravan;

/// <summary>How long a bean used by the <c>bean</c> EIP or component is kept (Camel's <c>BeanScope</c>).</summary>
public enum BeanScope
{
    /// <summary>One bean for the endpoint or node: looked up or created once (default).</summary>
    Singleton,

    /// <summary>One bean per exchange: looked up or created once for each exchange.</summary>
    Request,

    /// <summary>A new bean (lookup or instance) for each call.</summary>
    Prototype,
}
