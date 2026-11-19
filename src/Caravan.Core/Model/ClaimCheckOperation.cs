namespace Caravan.Model;

/// <summary>The operation of a <c>claimCheck</c> (Camel's <c>ClaimCheckOperation</c>).</summary>
public enum ClaimCheckOperation
{
    /// <summary>Merges back the data stored under the key (which stays stored).</summary>
    Get,

    /// <summary>Merges back the data stored under the key and removes it.</summary>
    GetAndRemove,

    /// <summary>Stores a copy of the exchange under the key, replacing any data stored under it.</summary>
    Set,

    /// <summary>Pushes a copy of the exchange on the stack.</summary>
    Push,

    /// <summary>Pops the latest pushed data and merges it back.</summary>
    Pop,
}
