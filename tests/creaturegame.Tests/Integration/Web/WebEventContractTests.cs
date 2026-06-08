using System.Collections;
using System.Reflection;
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
        var eventTypes = typeof(BattleEvent)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BattleEvent)) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(eventTypes); // guard against a reflection miss hiding everything

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
