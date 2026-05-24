using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Gen 1 Pokemon Battle Simulator ===");
        Console.WriteLine("Featuring: STAB, type effectiveness, priority & speed turn order\n");

        var moveContext = new MovesDbContext();
        moveContext.EnsureDatabaseCreated();

        var pokemonContext = new PokemonDbContext();
        pokemonContext.EnsureDatabaseCreated();

        var attackService = new AttackService(moveContext);
        var pokemonService = new PokemonService(pokemonContext);

        var typeChart = new Gen1TypeChart();

        // --- Bulbasaur (Grass/Poison) ---
        var bulbasaurSpecies = await pokemonService.GetSpeciesByNameAsync("bulbasaur");
        var bulbasaur = new Creature("Bulbasaur")
        {
            Level = 50,
            DvAttack = 15, DvDefense = 15, DvSpecial = 15, DvSpeed = 15, DvHP = 15
        };
        if (bulbasaurSpecies != null)
        {
            bulbasaur.InitializeFromSpecies(bulbasaurSpecies);
        }
        else
        {
            Console.WriteLine("[WARN] Bulbasaur not found in DB — using fallback stats.");
            bulbasaur.BaseHP = 45; bulbasaur.BaseAttack = 49; bulbasaur.BaseDefense = 49;
            bulbasaur.BaseSpecial = 65; bulbasaur.BaseSpeed = 45;
            bulbasaur.Type1 = DamageType.Grass; bulbasaur.Type2 = DamageType.Poison;
            bulbasaur.CalculateStats();
        }

        // Razor Leaf: Grass physical — STAB on Bulbasaur (1.5x), neutral vs Dragonite
        var razorLeaf = await attackService.GetAttackByNameAsync("razor-leaf")
            ?? new Attack("Razor Leaf", "Cuts the foe with sharp leaves.") { BaseDamage = 55, Accuracy = 95, AttackType = AttackType.Physical, DamageType = DamageType.Grass };

        // Quick Attack: Normal, Priority +1 — demonstrates priority system
        var quickAttack = await attackService.GetAttackByNameAsync("quick-attack")
            ?? new Attack("Quick Attack", "An extremely fast attack.") { BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical, DamageType = DamageType.Normal, Priority = 1 };

        bulbasaur.AddAttack(razorLeaf);
        bulbasaur.AddAttack(quickAttack);

        // --- Dragonite (Dragon/Flying) ---
        var dragoniteSpecies = await pokemonService.GetSpeciesByNameAsync("dragonite");
        var dragonite = new Creature("Dragonite")
        {
            Level = 50,
            DvAttack = 15, DvDefense = 15, DvSpecial = 15, DvSpeed = 15, DvHP = 15
        };
        if (dragoniteSpecies != null)
        {
            dragonite.InitializeFromSpecies(dragoniteSpecies);
        }
        else
        {
            Console.WriteLine("[WARN] Dragonite not found in DB — using fallback stats.");
            dragonite.BaseHP = 91; dragonite.BaseAttack = 134; dragonite.BaseDefense = 95;
            dragonite.BaseSpecial = 100; dragonite.BaseSpeed = 80;
            dragonite.Type1 = DamageType.Dragon; dragonite.Type2 = DamageType.Flying;
            dragonite.CalculateStats();
        }

        // Flamethrower: Fire special — SUPER EFFECTIVE vs Bulbasaur (Grass) = 2x
        // Dragonite has no Fire STAB so no 1.5x, but 2x type effectiveness is clearly visible
        var flamethrower = await attackService.GetAttackByNameAsync("flamethrower")
            ?? new Attack("Flamethrower", "A powerful fire attack that may burn.") { BaseDamage = 95, Accuracy = 100, AttackType = AttackType.Special, DamageType = DamageType.Fire };

        // Hyper Beam: Normal special — high power, no type bonus, shows contrast
        var hyperBeam = await attackService.GetAttackByNameAsync("hyper-beam")
            ?? new Attack("Hyper Beam", "A powerful beam attack.") { BaseDamage = 150, Accuracy = 90, AttackType = AttackType.Special, DamageType = DamageType.Normal };
        
        dragonite.AddAttack(flamethrower);
        dragonite.AddAttack(hyperBeam);

        // --- Type effectiveness preview ---
        Console.WriteLine("=== Type Effectiveness Preview ===");
        double fireVsGrass = typeChart.GetMultiplier(DamageType.Fire, DamageType.Grass);
        double grassVsDragon = typeChart.GetMultiplier(DamageType.Grass, DamageType.Dragon);
        Console.WriteLine($"  Fire   → Grass  : {fireVsGrass}x  (Flamethrower vs Bulbasaur — SUPER EFFECTIVE)");
        Console.WriteLine($"  Grass  → Dragon : {grassVsDragon}x  (Razor Leaf vs Dragonite — neutral)");
        Console.WriteLine($"  Bulbasaur STAB on Razor Leaf (Grass): 1.5x");
        Console.WriteLine();

        // --- Contestants ---
        Console.WriteLine("=== Battle Contestants ===");
        bulbasaur.DisplayInfo();
        Console.WriteLine($"  Moves: {string.Join(", ", bulbasaur.MoveSet.Select(m => $"{m.Base.Name} [{m.Base.DamageType}, Prio:{m.Base.Priority}, PP:{m.PowerPointsCurrent}]"))}");
        Console.WriteLine();
        dragonite.DisplayInfo();
        Console.WriteLine($"  Moves: {string.Join(", ", dragonite.MoveSet.Select(m => $"{m.Base.Name} [{m.Base.DamageType}, PP:{m.PowerPointsCurrent}]"))}");

        Console.WriteLine("\n=== Battle Start! ===");
        var battle = new Battle(bulbasaur, dragonite, typeChart,
            playerInput: AutoSelectInput.Instance,
            enemyInput:  AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}