using creaturegame.DB;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Pins the Gen 1 move descriptions in the live <c>moves.db</c> (DATA_IMPORT.md §4.1).</summary>
public class MoveDescriptionDataContractTests
{
    [Fact]
    public void EveryOverriddenMove_StoresTheImportersGen1Text()
    {
        using var ctx = new MovesDbContext();
        var stored = ctx.Moves.AsNoTracking().ToDictionary(m => m.Name!, m => m.Description);

        Assert.All(
            MoveImport.Gen1Descriptions,
            entry => Assert.Equal(entry.Value, stored[entry.Key])
        );
    }

    [Theory]
    [InlineData("Special Defense")]
    [InlineData("Special Attack")]
    [InlineData("Dive")]
    [InlineData("Dig users")]
    [InlineData("in the air")]
    [InlineData("fleeing")]
    [InlineData("switch")]
    [InlineData("five turns")]
    [InlineData("five times")]
    [InlineData("heavier")]
    [InlineData("1/3")]
    [InlineData("half the damage it would")]
    [InlineData("Disables the target’s last used move")]
    [InlineData("Copies the target’s last used move")]
    [InlineData("dodging all attacks")]
    public void NoStoredDescription_NamesAModernEffect(string modernPhrase)
    {
        using var ctx = new MovesDbContext();
        var offenders = ctx
            .Moves.AsNoTracking()
            .ToList()
            .Where(m => m.Description!.Contains(modernPhrase))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("sky-attack", "flinch")]
    [InlineData("skull-bash", "Defense")]
    [InlineData("dizzy-punch", "confuse")]
    [InlineData("rock-slide", "flinch")]
    [InlineData("waterfall", "flinch")]
    [InlineData("tri-attack", "burn")]
    [InlineData("tri-attack", "freeze")]
    [InlineData("tri-attack", "paralyze")]
    public void MoveWithNoGen1SideEffect_DoesNotPromiseOne(string moveName, string laterEffect)
    {
        using var ctx = new MovesDbContext();
        var description = ctx.Moves.AsNoTracking().Single(m => m.Name == moveName).Description;

        Assert.DoesNotContain(laterEffect, description);
    }

    [Fact]
    public void FocusEnergy_WarnsAboutItsBug()
    {
        using var ctx = new MovesDbContext();
        var description = ctx
            .Moves.AsNoTracking()
            .Single(m => m.Name == "focus-energy")
            .Description;

        Assert.Contains("bug lowers it", description);
    }

    [Fact]
    public void Teleport_StatesThatItDoesNothingInOurGame()
    {
        using var ctx = new MovesDbContext();
        var teleport = ctx.Moves.AsNoTracking().Single(m => m.Name == "teleport");

        Assert.Equal("Has no effect.", teleport.Description);
    }
}
