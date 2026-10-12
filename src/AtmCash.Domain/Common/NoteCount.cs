namespace AtmCash.Domain.Common;

/// <summary>A number of banknotes of a single denomination.</summary>
public sealed record NoteCount(int Denomination, int Count)
{
    public int Amount => Denomination * Count;
}
