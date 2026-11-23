using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>One side's last selected and last loaded move, kept across a switch (pokered's <c>w*SelectedMove</c> and <c>w*MoveNum</c> data). See GENERATION_SEAMS.md §2 (Counter).</summary>
public sealed class MoveRecord
{
    public Attack? Selected { get; set; }
    public Attack? Loaded { get; set; }
}
