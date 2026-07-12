using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// Contract test for the engine→client seam: <see cref="SignalRBattleEventEmitter"/> maps every
/// <see cref="BattleEvent"/> the engine can emit to a named client event. Its <c>switch</c> ends in a
/// <c>_ =&gt; ("Unknown", …)</c> fallback, so an event type added to the engine but forgotten here would
/// silently reach the web client as "Unknown" and never render. This reflects over every concrete
/// BattleEvent subtype and fails loudly if any is unmapped or mis-named — so adding an event to the
/// engine forces the web mapping to keep up.
/// </summary>
public class WebEventContractTests
{
    [Fact]
    public void EveryBattleEventMapsToItsOwnNamedClientEvent()
    {
        var eventTypes = ConcreteBattleEventTypes();

        var problems = new List<string>();
        foreach (var type in eventTypes)
        {
            var evt = (BattleEvent)Instantiate(type);
            var (mappedType, payload) = SignalRBattleEventEmitter.MapEvent(evt);

            if (mappedType == "Unknown")
                problems.Add($"{type.Name}: not mapped (falls through to \"Unknown\")");
            else if (mappedType != type.Name)
                problems.Add($"{type.Name}: mapped to \"{mappedType}\" (expected \"{type.Name}\")");
            else if (payload is null)
                problems.Add($"{type.Name}: null payload");
        }

        Assert.True(
            problems.Count == 0,
            $"SignalR event mapping is incomplete:\n  {string.Join("\n  ", problems)}"
        );
    }

    /// <summary>
    /// Contract test for the <b>third</b> leg of the event map: the frontend timeline. Every
    /// <see cref="BattleEvent"/> the engine emits reaches <c>ClientApp/src/battle/timeline.ts</c>'s
    /// <c>expandEvent</c> switch by its type name; that switch ends in <c>default: {}</c>, so an event added
    /// to the engine but forgotten in <c>timeline.ts</c> would silently fall through and never render in the
    /// battle log. The two C# legs are guarded by the test above + <see cref="SignalRBattleEventEmitter"/>'s
    /// own "Unknown" fallback; the TS leg has no compiler to catch it, so this asserts each event name has a
    /// <c>case '&lt;Name&gt;'</c> arm. (Presence of the arm is the guard — a present arm that intentionally
    /// returns an empty expansion for one branch, like BattleEnded on a loss, is fine; falling through to
    /// <c>default</c> is not.)
    /// </summary>
    [Fact]
    public void EveryBattleEventHasATimelineArm()
    {
        var timeline = ReadTimelineSource();

        var unhandled = ConcreteBattleEventTypes()
            .Select(t => t.Name)
            // The case labels are written `case 'Name':` / `case 'Name' {` — match including the closing
            // quote so a shorter name can't match a longer one's label (e.g. 'Substitute' vs 'SubstitutePutUp').
            .Where(name => !timeline.Contains($"case '{name}'"))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            unhandled.Count == 0,
            "These BattleEvents have no expandEvent arm in timeline.ts (they fall through `default: {}` and "
                + "never render). Add a `case '<Name>'` for each:\n  "
                + string.Join("\n  ", unhandled)
        );
    }

    /// <summary>
    /// Field-level guard for the <see cref="TurnStarted"/> move projection: <see cref="MapEvent"/> hand-maps
    /// each <see cref="MoveInfo"/> into an anonymous object, so a field added to <c>MoveInfo</c> (here:
    /// <c>Stab</c>, which drives the move-menu STAB highlight) is silently dropped unless it's listed in the
    /// projection. The reflection-based contract test above instantiates events with an *empty* move list, so
    /// it can't catch this. Pins the move fields the client actually reads.
    /// </summary>
    [Fact]
    public void TurnStarted_MoveProjection_CarriesStabFlag()
    {
        var move = new MoveInfo(
            "flamethrower",
            DamageType.Fire,
            15,
            15,
            Disabled: false,
            Stab: true
        );
        var evt = new TurnStarted(
            1,
            "PLAYER",
            100,
            100,
            StatusCondition.None,
            0,
            100,
            "ENEMY",
            80,
            80,
            StatusCondition.None,
            new[] { move }
        );

        var (_, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var firstMove = doc.RootElement.GetProperty("Moves")[0];

        Assert.True(
            firstMove.TryGetProperty("Stab", out var stab),
            $"TurnStarted move projection dropped the Stab field — the move menu can't show STAB. Payload: {doc.RootElement}"
        );
        Assert.True(stab.GetBoolean());
    }

    /// <summary>Same projection guard for <see cref="MoveInfo.Effectiveness"/> — the ×N effectiveness pill
    /// can't render if the field is dropped on the wire (the failure mode the STAB flag hit).</summary>
    [Fact]
    public void TurnStarted_MoveProjection_CarriesEffectiveness()
    {
        var move = new MoveInfo(
            "flamethrower",
            DamageType.Fire,
            15,
            15,
            Disabled: false,
            Stab: false,
            Effectiveness: 2.0
        );
        var evt = new TurnStarted(
            1,
            "PLAYER",
            100,
            100,
            StatusCondition.None,
            0,
            100,
            "ENEMY",
            80,
            80,
            StatusCondition.None,
            new[] { move }
        );

        var (_, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var firstMove = doc.RootElement.GetProperty("Moves")[0];

        Assert.True(
            firstMove.TryGetProperty("Effectiveness", out var eff),
            $"TurnStarted move projection dropped the Effectiveness field — the move menu can't show the ×N pill. Payload: {doc.RootElement}"
        );
        Assert.Equal(2.0, eff.GetDouble());
    }

    /// <summary>Field-level guard for the <see cref="CreatureEvolved"/> projection: the client needs both
    /// names and both species ids (from → to) to render the morph. The reflection contract test only checks
    /// the event is mapped, not that every field survives the hand-written projection — this pins them.</summary>
    [Fact]
    public void CreatureEvolved_Projection_CarriesBothFormsAndSpeciesIds()
    {
        var evt = new CreatureEvolved("CHARMANDER", "CHARMELEON", 4, 5);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("CreatureEvolved", type);
        Assert.Equal("CHARMANDER", root.GetProperty("FromName").GetString());
        Assert.Equal("CHARMELEON", root.GetProperty("ToName").GetString());
        Assert.Equal(4, root.GetProperty("FromSpeciesId").GetInt32());
        Assert.Equal(5, root.GetProperty("ToSpeciesId").GetInt32());
    }

    /// <summary>Field-level guard for the <see cref="EvolutionOffered"/> projection: the cancel modal needs
    /// both names + species ids to render. The reflection contract test only checks it's mapped.</summary>
    [Fact]
    public void EvolutionOffered_Projection_CarriesBothFormsAndSpeciesIds()
    {
        var evt = new EvolutionOffered("CHARMANDER", "CHARMELEON", 4, 5);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("EvolutionOffered", type);
        Assert.Equal("CHARMANDER", root.GetProperty("FromName").GetString());
        Assert.Equal("CHARMELEON", root.GetProperty("ToName").GetString());
        Assert.Equal(4, root.GetProperty("FromSpeciesId").GetInt32());
        Assert.Equal(5, root.GetProperty("ToSpeciesId").GetInt32());
    }

    /// <summary>Field-level guard for the <see cref="BiomeChoiceOffered"/> projection: each
    /// <see cref="BiomeOption"/> is hand-mapped into an anonymous object, so the map screen's id/name/type-badge
    /// fields are silently dropped unless listed. The reflection contract test instantiates the event with an
    /// <i>empty</i> options list, so it can't catch this — pins the nested sub-fields the 3b-2 map UI reads.</summary>
    [Fact]
    public void BiomeChoiceOffered_Projection_CarriesOptionSubFields()
    {
        var evt = new BiomeChoiceOffered([
            new BiomeOption(
                "phantom-marsh",
                "Phantom Marsh",
                [DamageType.Ghost, DamageType.Poison]
            ),
        ]);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var option = doc.RootElement.GetProperty("Options")[0];

        Assert.Equal("BiomeChoiceOffered", type);
        Assert.Equal("phantom-marsh", option.GetProperty("Id").GetString());
        Assert.Equal("Phantom Marsh", option.GetProperty("Name").GetString());
        Assert.Equal("Ghost", option.GetProperty("Types")[0].GetString());
        Assert.Equal("Poison", option.GetProperty("Types")[1].GetString());
    }

    /// <summary>Same projection guard for <see cref="BiomeEntered"/> — the client titles/themes the next leg
    /// from these fields, dropped on the wire if not listed (the failure mode the STAB flag hit).</summary>
    [Fact]
    public void BiomeEntered_Projection_CarriesAllFields()
    {
        var evt = new BiomeEntered(
            "phantom-marsh",
            "Phantom Marsh",
            [DamageType.Ghost, DamageType.Poison]
        );

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("BiomeEntered", type);
        Assert.Equal("phantom-marsh", root.GetProperty("BiomeId").GetString());
        Assert.Equal("Phantom Marsh", root.GetProperty("BiomeName").GetString());
        Assert.Equal("Ghost", root.GetProperty("Types")[0].GetString());
    }

    /// <summary>Field-level guard for the <see cref="CreatureFled"/> projection: the client picks the flee
    /// wording from <c>IsPlayer</c> ("… was blown away!" vs "The wild … fled!"), so a dropped flag would
    /// silently mis-word every flee. The reflection contract test only checks the event is mapped.</summary>
    [Fact]
    public void CreatureFled_Projection_CarriesNameAndIsPlayer()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(
            new CreatureFled("PIDGEY", IsPlayer: true)
        );
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("CreatureFled", type);
        Assert.Equal("PIDGEY", root.GetProperty("Name").GetString());
        Assert.True(root.GetProperty("IsPlayer").GetBoolean());
    }

    /// <summary>Projection guard for <see cref="RunNodeEntered"/>: the client titles the node from
    /// <c>Kind</c> (the reflection contract test instantiates it with an empty string, so this pins a real
    /// value round-trips).</summary>
    [Fact]
    public void RunNodeEntered_Projection_CarriesKind()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(new RunNodeEntered("BossBattle"));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        Assert.Equal("RunNodeEntered", type);
        Assert.Equal("BossBattle", doc.RootElement.GetProperty("Kind").GetString());
    }

    /// <summary>Field-level guard for the <see cref="BiomeNodePlanRevealed"/> projection: the encounter-map
    /// overlay draws the biome's node ladder from this ordered list, dropped on the wire if not projected. The
    /// reflection contract test instantiates it with an <i>empty</i> list, so this pins the real values.</summary>
    [Fact]
    public void BiomeNodePlanRevealed_Projection_CarriesOrderedKinds()
    {
        var evt = new BiomeNodePlanRevealed(["WildBattle", "Shop", "BossBattle"]);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var kinds = doc.RootElement.GetProperty("NodeKinds");

        Assert.Equal("BiomeNodePlanRevealed", type);
        Assert.Equal("WildBattle", kinds[0].GetString());
        Assert.Equal("Shop", kinds[1].GetString());
        Assert.Equal("BossBattle", kinds[2].GetString());
    }

    /// <summary>Field-level guard for the <see cref="RegionMapRevealed"/> projection: each
    /// <see cref="RegionMapBiome"/> is hand-mapped, so the map's id/name/type-badge/edge fields are silently
    /// dropped unless listed. The reflection contract test instantiates the event with an <i>empty</i> biome
    /// list, so it can't catch this — pins the nested sub-fields (incl. the neighbour-id edges).</summary>
    [Fact]
    public void RegionMapRevealed_Projection_CarriesBiomeSubFieldsAndEdges()
    {
        var evt = new RegionMapRevealed([
            new RegionMapBiome(
                "phantom-marsh",
                "Phantom Marsh",
                [DamageType.Ghost, DamageType.Poison],
                ["mire-swamp", "haunted-spire"],
                MapX: 54,
                MapY: 84
            ),
        ]);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var biome = doc.RootElement.GetProperty("Biomes")[0];

        Assert.Equal("RegionMapRevealed", type);
        Assert.Equal("phantom-marsh", biome.GetProperty("Id").GetString());
        Assert.Equal("Phantom Marsh", biome.GetProperty("Name").GetString());
        Assert.Equal("Ghost", biome.GetProperty("Types")[0].GetString());
        Assert.Equal("mire-swamp", biome.GetProperty("Neighbours")[0].GetString());
        Assert.Equal("haunted-spire", biome.GetProperty("Neighbours")[1].GetString());
        // The authored map coords ride the wire too (the region-map overlay positions waypoints from them).
        Assert.Equal(54, biome.GetProperty("MapX").GetInt32());
        Assert.Equal(84, biome.GetProperty("MapY").GetInt32());
    }

    /// <summary>Field-level guard for the <see cref="RewardGranted"/> projection: the client reads the source,
    /// the gold delta + running total, and the item-name list to drive the HUD/log/modal. The reflection
    /// contract test instantiates the event with an <i>empty</i> item list, so it can't catch a dropped
    /// <c>ItemNames</c> — this pins the real values round-trip.</summary>
    [Fact]
    public void RewardGranted_Projection_CarriesSourceGoldTotalsAndItemNames()
    {
        var evt = new RewardGranted("Treasure", 40, 165, new[] { "Potion", "Ether" });

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("RewardGranted", type);
        Assert.Equal("Treasure", root.GetProperty("Source").GetString());
        Assert.Equal(40, root.GetProperty("Gold").GetInt32());
        Assert.Equal(165, root.GetProperty("GoldTotal").GetInt32());
        var names = root.GetProperty("ItemNames")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Equal(new[] { "Potion", "Ether" }, names);
    }

    /// <summary>Field-level guard for the <see cref="RewardChoiceOffered"/> projection: each
    /// <see cref="RewardOption"/> is hand-mapped by <c>ProjectRewardOption</c> into a flat discriminated shape, so
    /// the pick-one-of-N modal's per-card fields are silently dropped (or the <c>Rarity</c> string mis-cased)
    /// unless listed. The reflection contract test instantiates the event with an <i>empty</i> options list, so it
    /// can't catch this — pins the sub-fields the reward modal reads and the PascalCase rarity the TS
    /// <c>RewardRarity</c> union depends on.</summary>
    [Fact]
    public void RewardChoiceOffered_Projection_CarriesOptionSubFields()
    {
        var evt = new RewardChoiceOffered(
            "Battle",
            [
                new ItemRewardOption(15, "Full Restore", RewardRarity.Rare),
                new HealRewardOption(24, CureStatus: true, RestoreLowPp: true, Label: "Quick Heal"),
                new GoldRewardOption(120),
            ]
        );

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("RewardChoiceOffered", type);
        Assert.Equal("Battle", root.GetProperty("Source").GetString());

        var options = root.GetProperty("Options");
        var item = options[0];
        Assert.Equal("item", item.GetProperty("Kind").GetString());
        Assert.Equal(15, item.GetProperty("ItemId").GetInt32());
        Assert.Equal("Full Restore", item.GetProperty("ItemName").GetString());
        // PascalCase — the exact casing the TS RewardRarity union matches on to colour the card.
        Assert.Equal("Rare", item.GetProperty("Rarity").GetString());

        // The heal arm: the modal card reads these to render "Quick Heal" + what it will restore.
        var heal = options[1];
        Assert.Equal("heal", heal.GetProperty("Kind").GetString());
        Assert.Equal(24, heal.GetProperty("HpRestore").GetInt32());
        Assert.True(heal.GetProperty("CureStatus").GetBoolean());
        Assert.True(heal.GetProperty("RestoreLowPp").GetBoolean());
        Assert.Equal("Quick Heal", heal.GetProperty("Label").GetString());

        var gold = options[2];
        Assert.Equal("gold", gold.GetProperty("Kind").GetString());
        Assert.Equal(120, gold.GetProperty("Gold").GetInt32());
    }

    /// <summary>Field-level guard for the <see cref="ShopOffered"/> projection: each <see cref="ShopOfferItem"/>
    /// is hand-mapped by <c>ProjectShopItem</c> into a flat shape, so the shop modal's per-row fields (id / name /
    /// price / rarity colour) and the header balance are silently dropped unless listed. The reflection contract
    /// test instantiates the event with an <i>empty</i> items list, so it can't catch this — pins the sub-fields
    /// the shop modal reads and the PascalCase rarity the TS <c>RewardRarity</c> union depends on.</summary>
    [Fact]
    public void ShopOffered_Projection_CarriesBalanceAndItemSubFields()
    {
        var evt = new ShopOffered(
            [new ShopOfferItem(17, "Super Potion", 20, RewardRarity.Uncommon)],
            Balance: 142
        );

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("ShopOffered", type);
        Assert.Equal(142, root.GetProperty("Balance").GetInt32());

        var item = root.GetProperty("Items")[0];
        Assert.Equal(17, item.GetProperty("ItemId").GetInt32());
        Assert.Equal("Super Potion", item.GetProperty("ItemName").GetString());
        Assert.Equal(20, item.GetProperty("Price").GetInt32());
        // PascalCase — the exact casing the TS RewardRarity union matches on to colour the row.
        Assert.Equal("Uncommon", item.GetProperty("Rarity").GetString());
    }

    /// <summary>Field-level guard for the <see cref="ShopItemPurchased"/> projection: the client updates the gold
    /// HUD + modal balance and logs the buy from these fields, dropped on the wire if not listed.</summary>
    [Fact]
    public void ShopItemPurchased_Projection_CarriesNamePriceAndBalance()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(
            new ShopItemPurchased("Super Potion", 20, 122)
        );
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("ShopItemPurchased", type);
        Assert.Equal("Super Potion", root.GetProperty("ItemName").GetString());
        Assert.Equal(20, root.GetProperty("Price").GetInt32());
        Assert.Equal(122, root.GetProperty("Balance").GetInt32());
    }

    // Concrete (non-abstract) BattleEvent subtypes — the exact set the engine can emit to the client.
    private static List<Type> ConcreteBattleEventTypes()
    {
        var eventTypes = typeof(BattleEvent)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BattleEvent)) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(eventTypes); // guard against a reflection miss hiding everything
        return eventTypes;
    }

    // Read timeline.ts via this test file's compile-time path (repo-relative, like MovesFixture's db lookup).
    private static string ReadTimelineSource([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!; // .../tests/creaturegame.Tests/Integration/Web
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
        var timelinePath = Path.Combine(
            repoRoot,
            "creaturegame.Web",
            "ClientApp",
            "src",
            "battle",
            "timeline.ts"
        );

        Assert.True(
            File.Exists(timelinePath),
            $"Could not locate timeline.ts at '{timelinePath}'. Expected it under creaturegame.Web/ClientApp/src/battle/."
        );
        return File.ReadAllText(timelinePath);
    }

    // Build a BattleEvent via its primary constructor with harmless default arguments — enough for
    // MapEvent's type-switch and payload projection (it only reads scalar/string/enum props and, for
    // TurnStarted, an empty move list).
    private static object Instantiate(Type type)
    {
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => DefaultArg(p.ParameterType)).ToArray();
        return ctor.Invoke(args);
    }

    private static object? DefaultArg(Type t)
    {
        if (t == typeof(string))
            return "";
        if (t.IsValueType)
            return Activator.CreateInstance(t); // 0 / false / default enum
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var elem = t.GetGenericArguments()[0];
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(elem));
        }
        return null;
    }
}
