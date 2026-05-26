using creaturegame.Creatures;

namespace creaturegame.Combat;

public sealed class ConsoleBattleEventEmitter : IBattleEventEmitter
{
    public static readonly ConsoleBattleEventEmitter Instance = new();
    private ConsoleBattleEventEmitter() { }

    public void Emit(BattleEvent evt)
    {
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

            case DamageDealt e:
                if (e.TypeEffectiveness == 0.0)
                {
                    Console.WriteLine($"It doesn't affect {e.TargetName}...");
                    break;
                }
                if (e.IsCrit) Console.WriteLine("A critical hit!");
                Console.WriteLine($"{e.TargetName} took {e.Damage} damage!");
                if (e.TypeEffectiveness >= 2.0)
                    Console.WriteLine("It's super effective!");
                else if (e.TypeEffectiveness is > 0 and <= 0.5)
                    Console.WriteLine("It's not very effective...");
                break;

            case RecoilDamage e:
                Console.WriteLine($"{e.SourceName} is hit by recoil! ({e.Damage} damage)");
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
                Console.WriteLine(e.Status switch
                {
                    StatusCondition.Burn      => $"{e.TargetName} was burned!",
                    StatusCondition.Paralysis => $"{e.TargetName} is paralyzed! It may be unable to move!",
                    StatusCondition.Poison    => $"{e.TargetName} was poisoned!",
                    StatusCondition.Sleep     => $"{e.TargetName} fell asleep!",
                    StatusCondition.Freeze    => $"{e.TargetName} was frozen solid!",
                    _                         => $"{e.TargetName} was afflicted with {e.Status}!"
                });
                break;

            case StatusDamage e:
                Console.WriteLine(e.Source switch
                {
                    StatusCondition.Burn   => $"{e.TargetName} is hurt by its burn! ({e.Damage} damage)",
                    StatusCondition.Poison => $"{e.TargetName} is hurt by poison! ({e.Damage} damage)",
                    _                      => $"{e.TargetName} took {e.Damage} status damage!"
                });
                break;

            case StatusCleared e:
                Console.WriteLine(e.WasStatus switch
                {
                    StatusCondition.Sleep  => $"{e.CreatureName} woke up!",
                    StatusCondition.Freeze => $"{e.CreatureName} thawed out!",
                    _                      => $"{e.CreatureName} is no longer affected by {e.WasStatus}!"
                });
                break;

            case ActionBlocked e:
                Console.WriteLine(e.Reason switch
                {
                    StatusCondition.Sleep     => $"{e.CreatureName} is fast asleep!",
                    StatusCondition.Freeze    => $"{e.CreatureName} is frozen solid!",
                    StatusCondition.Paralysis => $"{e.CreatureName} is fully paralyzed! It can't move!",
                    _                         => $"{e.CreatureName} is immobilized by {e.Reason}!"
                });
                break;

            case CreatureFainted e:
                Console.WriteLine($"{e.Name} fainted!");
                break;
        }
    }
}
