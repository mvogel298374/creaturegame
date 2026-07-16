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
/// Contract tests for the engine→client seam. Every <see cref="BattleEvent"/> is hand-carried across three
/// layers — the record here, <see cref="SignalRBattleEventEmitter.MapEvent"/>'s anonymous-object projection,
/// and a <c>case</c> arm in <c>timeline.ts</c> — so the wire contract is only as good as what these tests pin.
/// Three kinds of guard live here, in widening order:
/// <list type="bullet">
/// <item><b>Name</b> (generic): every event maps to its own named client event and has a timeline arm — so a
/// <em>new event</em> can't be forgotten (it would reach the client as "Unknown", or fall through
/// <c>default: {}</c> and never render).</item>
/// <item><b>Field</b> (generic): every event projects <em>all</em> of its properties — including those of nested
/// payload records (<c>MoveInfo</c>, <c>PartyMemberInfo</c>, …) and of every variant of a union family
/// (<c>RewardOption</c>), each of which is hand-mapped in its own arm and so is its own place for a field to go
/// missing. This is what stops a <em>field added to an existing event</em> being silently dropped on the wire.</item>
/// <item><b>Value</b> (per event): the one-off <c>*_Projection_Carries*</c> tests, pinning what the generic
/// field check can't — projected values and their semantics (enums cast to strings, sub-field meaning).</item>
/// </list>
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
    /// The <b>field-level</b> counterpart to the two name-level guards above, and the general form of the
    /// one-off <c>*_Projection_Carries*</c> tests that follow. <see cref="MapEvent"/> hand-lists each event's
    /// fields into an anonymous object, so <em>adding a property to an existing event record</em> compiles,
    /// emits, and passes every other gate while the property silently never reaches the client — the name-level
    /// tests only prove the event itself is mapped, not that it is mapped <em>completely</em>.
    /// <para>This reflects over every concrete event and asserts each of its properties appears on the projected
    /// payload under its own name. A property that is deliberately renamed or withheld must be registered in
    /// <see cref="ProjectionExceptions"/> with a reason — so the omission becomes a decision on the record
    /// rather than an oversight.</para>
    /// </summary>
    [Fact]
    public void EveryBattleEventProjectsAllOfItsFields()
    {
        var problems = new List<string>();

        foreach (var type in ConcreteBattleEventTypes())
        {
            var evt = (BattleEvent)Instantiate(type);
            var (_, payload) = SignalRBattleEventEmitter.MapEvent(evt);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            CheckProjection(type, doc.RootElement, type.Name, problems);
        }

        Assert.True(
            problems.Count == 0,
            "SignalR event projection drops record fields — each is a field the engine sets and the client "
                + "never sees. Add it to the event's arm in SignalRBattleEventEmitter.MapEvent, or register it "
                + $"in ProjectionExceptions with a reason:\n  {string.Join("\n  ", problems)}"
        );
    }

    /// <summary>
    /// Asserts every property of <paramref name="recordType"/> appears on its projected <paramref name="payload"/>
    /// object, then recurses through nested payload records (a <c>MoveInfo</c> in <c>TurnStarted.PlayerMoves</c>, a
    /// <c>PartyMemberInfo</c> in a party snapshot, …). The nested leg is the one that matters most in practice:
    /// the sub-records are hand-mapped in their own inline <c>Select(… => new { … })</c>, which is exactly where a
    /// newly added field goes missing.
    /// </summary>
    private static void CheckProjection(
        Type recordType,
        JsonElement payload,
        string path,
        List<string> problems
    )
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return;

        var projected = payload
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

        foreach (var prop in EventProperties(recordType))
        {
            string name = prop.Name;

            if (ProjectionExceptions.TryGetValue((recordType.Name, prop.Name), out var expected))
            {
                // A registered omission (null) is asserted *absent*, so quietly re-adding it later trips the
                // test too — the exception list can't rot into a blanket mute.
                if (expected is null)
                {
                    if (projected.ContainsKey(prop.Name))
                        problems.Add(
                            $"{path}.{prop.Name}: registered as a deliberate omission but IS projected "
                                + "— drop the ProjectionExceptions entry."
                        );
                    continue;
                }
                name = expected;
            }

            if (!projected.TryGetValue(name, out var value))
            {
                problems.Add(
                    $"{path}.{prop.Name}: not projected by MapEvent — the client never receives it "
                        + $"(payload has: {string.Join(", ", projected.Keys)})."
                );
                continue;
            }

            // Recurse into nested payload records. A collection is probed with one element per expected type
            // (see ProbeElementTypes) and the projection is an order-preserving Select, so element i is checked
            // against expected type i — that is what lets a union family like RewardOption be checked variant
            // by variant.
            var elementTypes = ProbeElementTypes(prop.PropertyType);
            if (elementTypes.Count > 0)
            {
                if (value.ValueKind != JsonValueKind.Array)
                    continue;
                int length = value.GetArrayLength();

                // The probe fills a list all-or-nothing (DefaultArg), so the only legitimate lengths are 0 (the
                // depth cap declined to build elements) or exactly one per expected type. Anything between means
                // the projection filtered or reordered the list — which would silently leave the variants that
                // fell off the end unchecked, so fail rather than check fewer.
                if (length != 0 && length != elementTypes.Count)
                {
                    problems.Add(
                        $"{path}.{name}: projection returned {length} element(s) for {elementTypes.Count} "
                            + "probe(s) — the list is filtered or reordered, so the per-variant correspondence "
                            + "is unsafe and some variants would go unchecked."
                    );
                    continue;
                }

                for (int i = 0; i < elementTypes.Count && i < length; i++)
                    CheckProjection(
                        elementTypes[i],
                        value[i],
                        $"{path}.{name}[{elementTypes[i].Name}]",
                        problems
                    );
            }
            else if (ProbeType(prop.PropertyType) is { } single)
            {
                CheckProjection(single, value, $"{path}.{name}", problems);
            }
        }
    }

    /// <summary>
    /// The element types a probed collection property is filled with, in order — <em>the single source both the
    /// probe builder (<see cref="DefaultArg"/>) and the checker (<see cref="CheckProjection"/>) read</em>, so the
    /// two can never disagree about what is in the list. Empty for a leaf collection (strings, enums) — nothing
    /// to recurse into.
    /// <para>An <b>abstract</b> element type is a union family (<c>RewardOption</c> → Item/Gold/Heal): the probe
    /// carries one element of <em>every</em> concrete variant, because <see cref="SignalRBattleEventEmitter"/>
    /// hand-maps each variant in its own arm — so each variant is its own place for a field to go missing, and
    /// probing only one would leave the rest unguarded. Ordered by name so the fill order is deterministic
    /// (<c>GetTypes()</c> order is not guaranteed).</para>
    /// </summary>
    private static IReadOnlyList<Type> ProbeElementTypes(Type collectionType)
    {
        if (!collectionType.IsGenericType || !typeof(IEnumerable).IsAssignableFrom(collectionType))
            return [];

        var elem = collectionType.GetGenericArguments()[0];
        if (elem.IsAbstract && elem.Assembly == typeof(BattleEvent).Assembly)
            return elem
                .Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(elem) && ProbeType(t) is not null)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

        return ProbeType(elem) is { } single ? [single] : [];
    }

    /// <summary>
    /// The record type to probe for a given type, or null when it is a leaf on the wire. Only engine-assembly,
    /// concrete, constructible, non-enum types qualify — strings/enums/primitives project to JSON scalars, and an
    /// abstract type can't be instantiated directly (its concrete variants are probed instead, via
    /// <see cref="ProbeElementTypes"/>).
    /// </summary>
    private static Type? ProbeType(Type t) =>
        t.Assembly == typeof(BattleEvent).Assembly
        && !t.IsAbstract
        && !t.IsEnum
        && t != typeof(string)
        && t.GetConstructors().Length > 0
            ? t
            : null;

    /// <summary>
    /// The registered departures from "every record property is projected under its own name", keyed by
    /// (event, property). Value = the field name on the payload, or <c>null</c> for a property deliberately
    /// withheld from the client. Every entry needs a reason — an unexplained entry is just the silent drop this
    /// test exists to prevent, moved into a dictionary.
    /// </summary>
    private static readonly Dictionary<
        (string Event, string Property),
        string?
    > ProjectionExceptions = new()
    {
        // The client's TurnStarted payload calls the move list `Moves` (the UI's own vocabulary); the record
        // says PlayerMoves because it sits beside the Enemy* fields. Pinned field-by-field by the
        // TurnStarted_MoveProjection_* tests below.
        [(nameof(TurnStarted), nameof(TurnStarted.PlayerMoves))] = "Moves",
    };

    // A record's own data properties. Records emit their compiler-generated EqualityContract as protected, so
    // the Public flag already excludes it — no filtering needed.
    private static IEnumerable<PropertyInfo> EventProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// Value-level guard for the <see cref="TurnStarted"/> move projection: <see cref="MapEvent"/> hand-maps
    /// each <see cref="MoveInfo"/> into an anonymous object. <see cref="EveryBattleEventProjectsAllOfItsFields"/>
    /// already proves every <c>MoveInfo</c> field is <em>present</em> on the wire; this pins that the values
    /// <em>arrive intact</em> — specifically <c>Stab</c>, which drives the move-menu STAB highlight and would be
    /// just as broken projected as a constant.
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

    /// <summary>Value-level guard for the <see cref="BiomeChoiceOffered"/> projection: each
    /// <see cref="BiomeOption"/> is hand-mapped into an anonymous object.
    /// <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves the id/name/type-badge fields are present;
    /// this pins the values the 3b-2 map UI reads — notably the type list projected as <em>strings</em>.</summary>
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

    /// <summary>Value-level guard for the <see cref="RegionMapRevealed"/> projection: each
    /// <see cref="RegionMapBiome"/> is hand-mapped.
    /// <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves the id/name/type-badge/edge fields are
    /// present; this pins their values — notably that the neighbour-id <em>edges</em> survive as a list, which
    /// is what makes the map a graph rather than a scatter of nodes.</summary>
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

    /// <summary>Value-level guard for the <see cref="RewardChoiceOffered"/> projection: each
    /// <see cref="RewardOption"/> variant is hand-mapped by <c>ProjectRewardOption</c> into a flat discriminated
    /// shape. <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves every variant's fields are present (it
    /// probes the union family variant by variant); this pins what that can't see — the <c>Kind</c> discriminator
    /// each card is chosen by, and the PascalCase <c>Rarity</c> string the TS <c>RewardRarity</c> union depends
    /// on (mis-cased, it is present but useless).</summary>
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

    /// <summary>Value-level guard for the <see cref="ShopOffered"/> projection: each <see cref="ShopOfferItem"/>
    /// is hand-mapped by <c>ProjectShopItem</c> into a flat shape.
    /// <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves the per-row fields and the header balance are
    /// present; this pins their values — notably the PascalCase rarity the TS <c>RewardRarity</c> union depends
    /// on (mis-cased, it is present but useless).</summary>
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

    /// <summary>Value-level guard for the <see cref="AcquisitionOffered"/> projection: the offer modal reads the
    /// offered creature's flat card fields (source / species id / name / level / types / max HP / party-full) and
    /// the nested current-party snapshot. <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves those
    /// fields are present; this pins their values — the string-cast types and each member's status string, which
    /// arrive present-but-wrong if the enum cast is dropped.</summary>
    [Fact]
    public void AcquisitionOffered_Projection_CarriesOfferedCreatureAndPartySubFields()
    {
        var evt = new AcquisitionOffered(
            "ThemedDraft",
            SpeciesId: 25,
            Name: "PIKACHU",
            Level: 12,
            Types: [DamageType.Electric],
            MaxHp: 34,
            PartyFull: true,
            Party:
            [
                new PartyMemberInfo(
                    4,
                    "CHARMANDER",
                    14,
                    20,
                    40,
                    StatusCondition.Burn,
                    IsLead: true
                ),
            ]
        );

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("AcquisitionOffered", type);
        Assert.Equal("ThemedDraft", root.GetProperty("Source").GetString());
        Assert.Equal(25, root.GetProperty("SpeciesId").GetInt32());
        Assert.Equal("PIKACHU", root.GetProperty("Name").GetString());
        Assert.Equal(12, root.GetProperty("Level").GetInt32());
        Assert.Equal("Electric", root.GetProperty("Types")[0].GetString());
        Assert.Equal(34, root.GetProperty("MaxHp").GetInt32());
        Assert.True(root.GetProperty("PartyFull").GetBoolean());

        var member = root.GetProperty("Party")[0];
        Assert.Equal(4, member.GetProperty("SpeciesId").GetInt32());
        Assert.Equal("CHARMANDER", member.GetProperty("Name").GetString());
        Assert.Equal(14, member.GetProperty("Level").GetInt32());
        Assert.Equal(20, member.GetProperty("Hp").GetInt32());
        Assert.Equal(40, member.GetProperty("MaxHp").GetInt32());
        // Status projects as its string name (not the enum number) so the client reads one shape everywhere.
        Assert.Equal("Burn", member.GetProperty("Status").GetString());
        Assert.True(member.GetProperty("IsLead").GetBoolean());
    }

    /// <summary>Value-level guard for the <see cref="PartyUpdated"/> projection: the roster panel re-renders from
    /// the member snapshot. <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves the member sub-fields are
    /// present; this pins their values — notably the string-projected status.</summary>
    [Fact]
    public void PartyUpdated_Projection_CarriesMemberSubFields()
    {
        var evt = new PartyUpdated([
            new PartyMemberInfo(6, "CHARIZARD", 36, 100, 120, StatusCondition.None, IsLead: true),
            new PartyMemberInfo(9, "BLASTOISE", 34, 0, 110, StatusCondition.None, IsLead: false),
        ]);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var members = doc.RootElement.GetProperty("Members");

        Assert.Equal("PartyUpdated", type);
        Assert.Equal(6, members[0].GetProperty("SpeciesId").GetInt32());
        Assert.Equal("CHARIZARD", members[0].GetProperty("Name").GetString());
        Assert.True(members[0].GetProperty("IsLead").GetBoolean());
        Assert.Equal(0, members[1].GetProperty("Hp").GetInt32()); // a fainted bench member reads HP 0
        Assert.False(members[1].GetProperty("IsLead").GetBoolean());
    }

    /// <summary>Field-level guard for the <see cref="CreatureAcquired"/> projection: the log line needs the name,
    /// species id, and — on a full-party swap — the replaced flag + released member's name.</summary>
    [Fact]
    public void CreatureAcquired_Projection_CarriesNameSpeciesReplacedAndReplacedName()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(
            new CreatureAcquired("PIKACHU", 25, Replaced: true, ReplacedName: "RATTATA")
        );
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("CreatureAcquired", type);
        Assert.Equal("PIKACHU", root.GetProperty("Name").GetString());
        Assert.Equal(25, root.GetProperty("SpeciesId").GetInt32());
        Assert.True(root.GetProperty("Replaced").GetBoolean());
        Assert.Equal("RATTATA", root.GetProperty("ReplacedName").GetString());
    }

    /// <summary>Field-level guard for the <see cref="LeadChoiceOffered"/> projection: the lead-select modal reads
    /// the roster snapshot (each member's sprite id / name / level / HP / status / lead flag). The reflection
    /// contract test instantiates it with an <i>empty</i> party list, so this pins the member sub-fields.</summary>
    [Fact]
    public void LeadChoiceOffered_Projection_CarriesPartyMemberSubFields()
    {
        var evt = new LeadChoiceOffered([
            new PartyMemberInfo(6, "CHARIZARD", 36, 100, 120, StatusCondition.None, IsLead: true),
            new PartyMemberInfo(9, "BLASTOISE", 34, 80, 110, StatusCondition.Poison, IsLead: false),
        ]);

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var party = doc.RootElement.GetProperty("Party");

        Assert.Equal("LeadChoiceOffered", type);
        Assert.Equal(6, party[0].GetProperty("SpeciesId").GetInt32());
        Assert.Equal("CHARIZARD", party[0].GetProperty("Name").GetString());
        Assert.True(party[0].GetProperty("IsLead").GetBoolean());
        Assert.Equal("Poison", party[1].GetProperty("Status").GetString()); // string-projected status
        Assert.False(party[1].GetProperty("IsLead").GetBoolean());
    }

    /// <summary>Field-level guard for the <see cref="LeadChanged"/> projection: the log line needs the new lead's
    /// name + species id.</summary>
    [Fact]
    public void LeadChanged_Projection_CarriesNameAndSpeciesId()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(new LeadChanged("BLASTOISE", 9));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("LeadChanged", type);
        Assert.Equal("BLASTOISE", root.GetProperty("Name").GetString());
        Assert.Equal(9, root.GetProperty("SpeciesId").GetInt32());
    }

    /// <summary>Value-level guard for the <see cref="SwitchInOffered"/> projection (Phase 4 Stage 3): the forced
    /// switch-in modal reads the roster snapshot and the fainted name for the title.
    /// <see cref="EveryBattleEventProjectsAllOfItsFields"/> proves the member sub-fields are present; this pins the
    /// <em>semantics</em> the modal depends on — a fainted member reading HP 0 (which is how the client knows to
    /// disable it) and the string-projected status.</summary>
    [Fact]
    public void SwitchInOffered_Projection_CarriesPartyMemberSubFieldsAndFaintedName()
    {
        var evt = new SwitchInOffered(
            [
                new PartyMemberInfo(6, "CHARIZARD", 40, 0, 130, StatusCondition.None, IsLead: true),
                new PartyMemberInfo(
                    9,
                    "BLASTOISE",
                    38,
                    90,
                    120,
                    StatusCondition.Poison,
                    IsLead: false
                ),
            ],
            FaintedName: "CHARIZARD"
        );

        var (type, payload) = SignalRBattleEventEmitter.MapEvent(evt);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("SwitchInOffered", type);
        Assert.Equal("CHARIZARD", root.GetProperty("FaintedName").GetString());
        var party = root.GetProperty("Party");
        Assert.Equal(0, party[0].GetProperty("Hp").GetInt32()); // the fainted lead reads HP 0 (client disables it)
        Assert.Equal(9, party[1].GetProperty("SpeciesId").GetInt32());
        Assert.Equal("BLASTOISE", party[1].GetProperty("Name").GetString());
        Assert.Equal("Poison", party[1].GetProperty("Status").GetString()); // string-projected status
        Assert.False(party[1].GetProperty("IsLead").GetBoolean());
    }

    /// <summary>Field-level guard for the <see cref="CreatureSwitchedIn"/> projection (Phase 4 Stage 3): the client
    /// swaps the player sprite (<c>SpeciesId</c>) and retargets the nameplate (<c>Name</c>/<c>Level</c>/HP/status)
    /// onto the incoming creature. <c>Level</c> especially must survive — <see cref="TurnStarted"/> carries none, so
    /// a dropped level would freeze the nameplate on the fainted creature's level.</summary>
    [Fact]
    public void CreatureSwitchedIn_Projection_CarriesNameSpeciesLevelHpAndStatus()
    {
        var (type, payload) = SignalRBattleEventEmitter.MapEvent(
            new CreatureSwitchedIn("BLASTOISE", 9, 38, 90, 120, StatusCondition.Poison)
        );
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("CreatureSwitchedIn", type);
        Assert.Equal("BLASTOISE", root.GetProperty("Name").GetString());
        Assert.Equal(9, root.GetProperty("SpeciesId").GetInt32());
        Assert.Equal(38, root.GetProperty("Level").GetInt32());
        Assert.Equal(90, root.GetProperty("Hp").GetInt32());
        Assert.Equal(120, root.GetProperty("MaxHp").GetInt32());
        Assert.Equal("Poison", root.GetProperty("Status").GetString());
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
    // MapEvent's type-switch and payload projection.
    private static object Instantiate(Type type) => Instantiate(type, depth: 0);

    private static object Instantiate(Type type, int depth)
    {
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => DefaultArg(p.ParameterType, depth)).ToArray();
        return ctor.Invoke(args);
    }

    // A harmless probe value for one constructor parameter. Collections are filled with one element per type in
    // ProbeElementTypes (the same source the checker reads), so the projection of a nested payload record —
    // MoveInfo, PartyMemberInfo, and every RewardOption variant — is actually exercised rather than skipped over
    // an empty list; those inline Select(… => new { … }) arms are where a dropped field hides. Depth-capped so a
    // self-referencing record can't recurse forever.
    private static object? DefaultArg(Type t, int depth)
    {
        if (t == typeof(string))
            return "";
        if (t.IsValueType)
            return Activator.CreateInstance(t); // 0 / false / default enum
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var elem = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem))!;
            if (depth < MaxProbeDepth)
                foreach (var probe in ProbeElementTypes(t))
                    list.Add(Instantiate(probe, depth + 1));
            return list;
        }
        if (depth < MaxProbeDepth && ProbeType(t) is { } nested)
            return Instantiate(nested, depth + 1);
        return null;
    }

    // Events nest payload records at most a couple of levels (event → PartyMemberInfo); the cap only exists so
    // a future self-referencing record can't spin the probe builder forever.
    private const int MaxProbeDepth = 4;
}
