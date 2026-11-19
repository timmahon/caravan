namespace Caravan.Testing.Notify;

/// <summary>How a group of <see cref="NotifyBuilder"/> predicates combines with the groups before it.</summary>
internal enum NotifyOperation
{
    /// <summary>Both must hold.</summary>
    And,

    /// <summary>Either must hold.</summary>
    Or,

    /// <summary>The groups before must hold and this one must not.</summary>
    Not,
}
