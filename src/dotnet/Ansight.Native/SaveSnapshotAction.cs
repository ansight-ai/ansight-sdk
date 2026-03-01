using System;
using System.Threading.Tasks;

namespace Ansight;

/// <summary>
/// Encapsulates a user-configured save snapshot action rendered in the slide sheet.
/// </summary>
public sealed class SaveSnapshotAction
{
    public SaveSnapshotAction(string label, Func<Snapshot, Task> copyDelegate)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        }

        Label = label;
        CopyDelegate = copyDelegate ?? throw new ArgumentNullException(nameof(copyDelegate));
    }

    /// <summary>
    /// Display label for the action button.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Delegate invoked with the exported snapshot payload.
    /// </summary>
    public Func<Snapshot, Task> CopyDelegate { get; }
}
