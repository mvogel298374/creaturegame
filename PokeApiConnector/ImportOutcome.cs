namespace PokeApiConnector;

/// <summary>One import stage's failures (urls or short descriptions); empty when the stage was clean.</summary>
public sealed record StageResult(string Stage, IReadOnlyList<string> Failures)
{
    public static StageResult FromCount(string stage, int failedCount) =>
        new(stage, failedCount > 0 ? [$"{failedCount} file(s) failed to download"] : []);
}

/// <summary>Turns a run's stage results into its summary and exit code — 0 only when every stage was clean.</summary>
public static class ImportOutcome
{
    public static int ExitCode(IEnumerable<StageResult> stages) =>
        stages.Any(s => s.Failures.Count > 0) ? 1 : 0;

    public static int Report(IReadOnlyList<StageResult> stages, string successMessage)
    {
        foreach (var stage in stages.Where(s => s.Failures.Count > 0))
        {
            Console.WriteLine($"\n{stage.Stage}: {stage.Failures.Count} failure(s):");
            foreach (var failure in stage.Failures)
                Console.WriteLine($"  {failure}");
        }

        int exitCode = ExitCode(stages);
        Console.WriteLine(exitCode == 0 ? $"\n{successMessage}" : "\nImport FAILED - see above.");
        return exitCode;
    }
}
