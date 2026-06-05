using System.ComponentModel.DataAnnotations;

namespace creaturegame.DB;

public class PokemonGameAvailability
{
    [Key]
    public int Id { get; set; }
    public int SpeciesId { get; set; }
    public string GameVersion { get; set; } = "";

    // Wild, Static, Gift, GameCorner, Trade, Event
    public string AvailabilityType { get; set; } = "Wild";

    public PokemonSpecies Species { get; set; } = null!;
}
