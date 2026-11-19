namespace Caravan.Components.File;

/// <summary>What a file producer does when the target file exists (Camel's <c>GenericFileExist</c>; the <c>fileExist</c> option).</summary>
public enum GenericFileExist
{
    /// <summary>Replace the file (default).</summary>
    Override,

    /// <summary>Append to the file.</summary>
    Append,

    /// <summary>Fail with a <see cref="GenericFileOperationFailedException"/>.</summary>
    Fail,

    /// <summary>Do nothing: the existing file is kept and the message is not written.</summary>
    Ignore,

    /// <summary>Move the existing file away first, to the name the <c>moveExisting</c> expression gives.</summary>
    Move,

    /// <summary>With a temporary file name: rename the temporary file to the target without checking whether it exists (fails if it does).</summary>
    TryRename,
}
