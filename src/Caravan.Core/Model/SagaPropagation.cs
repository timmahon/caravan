namespace Caravan.Model;

/// <summary>How a <c>saga</c> EIP relates to the saga the exchange is already part of (Camel's <c>SagaPropagation</c>).</summary>
public enum SagaPropagation
{
    /// <summary>Joins the current saga, or starts a new one when there is none (the default).</summary>
    Required,

    /// <summary>Always starts a new saga; the current one is suspended for the block and resumed after it.</summary>
    RequiresNew,

    /// <summary>Joins the current saga; fails the exchange when there is none.</summary>
    Mandatory,

    /// <summary>Joins the current saga when there is one; otherwise the block runs outside any saga.</summary>
    Supports,

    /// <summary>The block runs outside any saga: the current one is suspended for the block and resumed after it.</summary>
    NotSupported,

    /// <summary>The block runs outside any saga; fails the exchange when there is a current saga.</summary>
    Never,
}
