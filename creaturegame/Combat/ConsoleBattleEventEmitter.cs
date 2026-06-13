using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A debug <see cref="IBattleEventEmitter"/> that narrates a battle to <see cref="Console"/> in Gen 1
/// flavour text — a developer aid for watching a unit test play out, not a production/client emitter
/// (the web client renders events via <c>timeline.ts</c>; this is never wired into the app).
/// </summary>
/// <remarks>
/// <para><b>Silent by default.</b> Output is gated on the <c>CG_BATTLE_LOG</c> environment variable so the
/// many tests that pass <see cref="Instance"/> as their emitter don't spam stdout on a normal run. With the
/// flag unset, <see cref="Emit"/> is a no-op.</para>
/// <para><b>To watch a battle narrate</b>, set the flag for the run (PowerShell):</para>
/// <code>
/// $env:CG_BATTLE_LOG = "1"
/// &amp; "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests --filter "FullyQualifiedName~Substitute"
/// </code>
/// <para>Filter to one test (or a small set) — every test that uses this emitter narrates while the flag is
/// on, so an unfiltered run is a wall of text. Any value other than <c>0</c>/<c>false</c> enables it.</para>
/// </remarks>
public sealed class ConsoleBattleEventEmitter : IBattleEventEmitter
{
    public static readonly ConsoleBattleEventEmitter Instance = new();

    // Read once: whether CG_BATTLE_LOG opts this run into battle narration. "0"/"false" (any case) count as
    // off, as does an unset/empty value; anything else is on. Cached so Emit stays allocation-free per event.
    private static readonly bool Verbose = ResolveVerbose();

    private static bool ResolveVerbose()
    {
        var flag = Environment.GetEnvironmentVariable("CG_BATTLE_LOG");
        if (string.IsNullOrWhiteSpace(flag))
            return false;
        return !flag.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !flag.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private ConsoleBattleEventEmitter() { }

    public void Emit(BattleEvent evt)
    {
        if (!Verbose)
            return;

        switch (evt)
        {
            case BattleStarted e:
                Console.WriteLine($"A wild {e.EnemyName} appeared!");
                Console.WriteLine($"Go! {e.PlayerName}!");
                break;

            case TurnStarted e:
                Console.WriteLine($"\n{e.PlayerName}: {e.PlayerHp}/{e.PlayerMaxHp} HP");
                Console.WriteLine($"{e.EnemyName}: {e.EnemyHp}/{e.EnemyMaxHp} HP");
                break;

            case TurnEnded:
                Console.WriteLine("\nPress any key for the next round...");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                break;

            case BattleEnded:
                break;

            case MoveUsed e:
                Console.WriteLine($"{e.AttackerName} used {e.MoveName}!");
                break;

            case MoveMissed:
                Console.WriteLine("The attack missed!");
                break;

            case MoveHadNoEffect e:
                Console.WriteLine($"It doesn't affect {e.TargetName}...");
                break;

            case ButNothingHappened:
                Console.WriteLine("But nothing happened!");
                break;

            case SubstitutePutUp e:
                Console.WriteLine($"{e.CreatureName} put up a substitute!");
                break;

            case SubstituteAbsorbedHit e:
                Console.WriteLine($"The substitute took damage for {e.CreatureName}!");
                break;

            case SubstituteFaded e:
                Console.WriteLine($"{e.CreatureName}'s substitute faded!");
                break;

            case DamageDealt e:
                if (e.TypeEffectiveness == 0.0)
                {
                    Console.WriteLine($"It doesn't affect {e.TargetName}...");
                    break;
                }
                if (e.IsCrit)
                    Console.WriteLine("A critical hit!");
                Console.WriteLine($"{e.TargetName} took {e.Damage} damage!");
                if (e.TypeEffectiveness >= 2.0)
                    Console.WriteLine("It's super effective!");
                else if (e.TypeEffectiveness is > 0 and <= 0.5)
                    Console.WriteLine("It's not very effective...");
                break;

            case RecoilDamage e:
                Console.WriteLine($"{e.SourceName} is hit by recoil! ({e.Damage} damage)");
                break;

            case CrashDamage e:
                Console.WriteLine($"{e.SourceName} kept going and crashed! ({e.Damage} damage)");
                break;

            case MoveDisabled e:
                Console.WriteLine($"{e.TargetName}'s {e.MoveName} was disabled!");
                break;

            case MoveReEnabled e:
                Console.WriteLine($"{e.CreatureName}'s {e.MoveName} is no longer disabled!");
                break;

            case MistApplied e:
                Console.WriteLine($"{e.CreatureName} became shrouded in mist!");
                break;

            case StatDropBlocked e:
                Console.WriteLine($"{e.CreatureName} is protected by the mist!");
                break;

            case MultiHitCompleted e:
                Console.WriteLine($"Hit {e.Hits} time{(e.Hits == 1 ? "" : "s")}!");
                break;

            case CoinsScattered:
                Console.WriteLine("Coins scattered everywhere!");
                break;

            case ConfusionStarted e:
                Console.WriteLine($"{e.TargetName} became confused!");
                break;

            case ConfusionMessage e:
                Console.WriteLine($"{e.CreatureName} is confused!");
                break;

            case ConfusionDamage e:
                Console.WriteLine("It hurt itself in its confusion!");
                Console.WriteLine($"{e.CreatureName} took {e.Damage} damage!");
                break;

            case ConfusionCleared e:
                Console.WriteLine($"{e.CreatureName} snapped out of confusion!");
                break;

            case StatusApplied e:
                Console.WriteLine(
                    e.Status switch
                    {
                        StatusCondition.Burn => $"{e.TargetName} was burned!",
                        StatusCondition.Paralysis =>
                            $"{e.TargetName} is paralyzed! It may be unable to move!",
                        StatusCondition.Poison => $"{e.TargetName} was poisoned!",
                        StatusCondition.Sleep => $"{e.TargetName} fell asleep!",
                        StatusCondition.Freeze => $"{e.TargetName} was frozen solid!",
                        _ => $"{e.TargetName} was afflicted with {e.Status}!",
                    }
                );
                break;

            case StatusDamage e:
                Console.WriteLine(
                    e.Source switch
                    {
                        StatusCondition.Burn =>
                            $"{e.TargetName} is hurt by its burn! ({e.Damage} damage)",
                        StatusCondition.Poison =>
                            $"{e.TargetName} is hurt by poison! ({e.Damage} damage)",
                        _ => $"{e.TargetName} took {e.Damage} status damage!",
                    }
                );
                break;

            case StatusCleared e:
                Console.WriteLine(
                    e.WasStatus switch
                    {
                        StatusCondition.Sleep => $"{e.CreatureName} woke up!",
                        StatusCondition.Freeze => $"{e.CreatureName} thawed out!",
                        _ => $"{e.CreatureName} is no longer affected by {e.WasStatus}!",
                    }
                );
                break;

            case ActionBlocked e:
                Console.WriteLine(
                    e.Reason switch
                    {
                        StatusCondition.Sleep => $"{e.CreatureName} is fast asleep!",
                        StatusCondition.Freeze => $"{e.CreatureName} is frozen solid!",
                        StatusCondition.Paralysis =>
                            $"{e.CreatureName} is fully paralyzed! It can't move!",
                        _ => $"{e.CreatureName} is immobilized by {e.Reason}!",
                    }
                );
                break;

            case StatStageChanged e:
                string direction = e.Delta > 0 ? "rose" : "fell";
                string amount = Math.Abs(e.Delta) >= 2 ? " sharply" : "";
                Console.WriteLine(
                    $"{e.CreatureName}'s {e.Stat}{amount} {direction}! (→ {e.NewStage:+0;-0;0})"
                );
                break;

            case HazeClearedStages:
                Console.WriteLine(
                    "A black mist swirled around all Pokémon! All stat changes were erased!"
                );
                break;

            case DrainHealed e:
                Console.WriteLine(
                    $"{e.SourceName} had its energy drained! ({e.HealAmount} HP restored)"
                );
                break;

            case Healed e:
                Console.WriteLine(
                    $"{e.CreatureName} regained health! ({e.HealAmount} HP restored)"
                );
                break;

            case MimicLearned e:
                Console.WriteLine($"{e.CreatureName} learned {e.MoveName}!");
                break;

            case TransformedInto e:
                Console.WriteLine($"{e.CreatureName} transformed into {e.TargetName}!");
                break;

            case ConvertedType e:
                Console.WriteLine($"{e.CreatureName} changed its type to {e.NewType}!");
                break;

            case ScreenApplied e:
                Console.WriteLine($"{e.CreatureName} was protected by {e.ScreenName}!");
                break;

            case FocusEnergyApplied e:
                Console.WriteLine($"{e.CreatureName} is getting pumped!");
                break;

            case BideStoring e:
                Console.WriteLine($"{e.CreatureName} is storing energy!");
                break;

            case LeechSeedApplied e:
                Console.WriteLine($"{e.TargetName} was seeded!");
                break;

            case LeechSeedDamage e:
                Console.WriteLine(
                    $"{e.DrainedName}'s health was sapped by Leech Seed! ({e.Damage} damage)"
                );
                break;

            case LeechSeedHealed e:
                Console.WriteLine(
                    $"{e.HealedName} absorbed energy from Leech Seed! (+{e.Amount} HP)"
                );
                break;

            case Recharging e:
                Console.WriteLine($"{e.CreatureName} must recharge!");
                break;

            case BindingStarted e:
                Console.WriteLine($"{e.TargetName} was squeezed by {e.MoveName}!");
                break;

            case BindingBlocked e:
                Console.WriteLine($"{e.CreatureName} is bound and can't move!");
                break;

            case FlinchBlocked e:
                Console.WriteLine($"{e.CreatureName} flinched and couldn't move!");
                break;

            case ChargingUp e:
                Console.WriteLine($"{e.CreatureName} is charging up {e.MoveName}!");
                break;

            case CreatureFainted e:
                Console.WriteLine($"{e.Name} fainted!");
                break;

            case ExperienceGained e:
                Console.WriteLine($"{e.CreatureName} gained {e.Amount} EXP. Points!");
                break;

            case LeveledUp e:
                Console.WriteLine($"{e.CreatureName} grew to level {e.NewLevel}!");
                break;

            case MoveLearned e:
                Console.WriteLine($"{e.CreatureName} learned {e.MoveName}!");
                break;

            case MoveReplacementRequired e:
                Console.WriteLine(
                    $"{e.CreatureName} is trying to learn {e.NewMoveName}, but already knows "
                        + $"{string.Join(", ", e.CurrentMoves)}."
                );
                break;

            case MoveForgotten e:
                Console.WriteLine($"{e.CreatureName} forgot {e.MoveName}!");
                break;

            case MoveLearnDeclined e:
                Console.WriteLine($"{e.CreatureName} did not learn {e.MoveName}.");
                break;

            case RunEnded e:
                Console.WriteLine(
                    $"Run over — {e.FinalCreatureName} fainted after {e.BattlesWon} win(s), reaching level {e.FinalLevel}."
                );
                break;

            case RecoveryOffered e:
                Console.WriteLine($"{e.CreatureName} reached a Poké Center! Heal up?");
                break;

            case PlayerRecovered e:
                Console.WriteLine($"{e.CreatureName} was fully healed!");
                break;

            case RecoveryDeclined e:
                Console.WriteLine($"{e.CreatureName} decided to keep going!");
                break;
        }
    }
}
