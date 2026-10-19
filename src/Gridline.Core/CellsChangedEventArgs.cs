namespace Gridline.Core;

/// <summary>Raised after a mutating call; carries every canonical address whose input or value changed (§9.2).</summary>
public sealed class CellsChangedEventArgs : EventArgs
{
    public CellsChangedEventArgs(IReadOnlyCollection<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        Addresses = addresses;
    }

    public IReadOnlyCollection<string> Addresses { get; }
}
