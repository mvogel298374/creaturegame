using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using creaturegame.Combat;
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
