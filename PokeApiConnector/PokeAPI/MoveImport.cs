using System.Text.Json;
using PokeApiConnector.Generation_1;
using creaturegame.Attacks;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

public class MoveImport
{
    public static async Task FetchMovesByGeneration(int generation)
    {
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";
        using HttpClient client = new HttpClient();

        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var genResponse = JsonSerializer.Deserialize<Gen1Response>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (genResponse?.moves != null)
            {
                using var context = new GameDbContext();
                foreach (var moveResource in genResponse.moves)
                {
                    await FetchMoveDataByUrl(moveResource.url, context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching moves by generation: {ex.Message}");
        }
    }
    
    public static async Task FetchMoveData(int moveId)
    {
        string url = $"https://pokeapi.co/api/v2/move/{moveId}/";
        using var context = new GameDbContext();
        await FetchMoveDataByUrl(url, context);
    }

    private static async Task FetchMoveDataByUrl(string url, GameDbContext context)
    {
        using HttpClient client = new HttpClient();
        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiMove pokeMove = JsonSerializer.Deserialize<PokeApiMove>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (pokeMove != null)
            {
                Attack attack = MapToAttack(pokeMove);
                
                var existingMove = await context.Moves.AsNoTracking().FirstOrDefaultAsync(m => m.Id == attack.Id);
                if (existingMove == null)
                {
                    context.Moves.Add(attack);
                    Console.WriteLine($"Imported New Move: {attack.Name} (ID: {attack.Id})");
                }
                else
                {
                    context.Moves.Update(attack);
                    Console.WriteLine($"Updated Existing Move: {attack.Name} (ID: {attack.Id})");
                }
                
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching move data from {url}: {ex.Message}");
        }
    }

    private static Attack MapToAttack(PokeApiMove pokeMove)
    {
        Attack attack = new Attack
        {
            Id = pokeMove.Id,
            Name = pokeMove.Name,
            BaseDamage = pokeMove.Power ?? 0,
            Accuracy = pokeMove.Accuracy ?? 100,
            PowerPointsMax = pokeMove.Pp ?? 30,
            Description = pokeMove.EffectEntries?
                .FirstOrDefault(e => e.Language.Name == "en")?.ShortEffect ?? "No description available."
        };

        if (Enum.TryParse<DamageType>(pokeMove.Type.Name, true, out var damageType))
        {
            attack.DamageType = damageType;
        }
        else
        {
            attack.DamageType = DamageType.Normal; // Default
        }

        attack.AttackType = pokeMove.DamageClass?.Name.ToLower() switch
        {
            "physical" => AttackType.Physical,
            "special" => AttackType.Special,
            _ => AttackType.Undefined
        };

        return attack;
    }
}