using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.NewAlgorithm.Plugin;

/// <summary>
///     Adapts the standalone share-debt weighting engine (SecRandom.NewAlgorithm) to the host
///     roll-call pipeline. Contract: emit weighted candidates only; winner selection, commit and
///     verification stay in the host pipeline.
///     Two orthogonal per-student levers, both surfaced through existing host UI:
///     内幕设置 (BehindScene attached settings) supplies the long-term frequency Multiplier —
///     0 excludes, 100 forces （必中）, intermediate p enters the engine as share multiplier p;
///     the plugin's 个人抽取上限 attached setting supplies the base Cap, a pure safety valve:
///     once a student's <c>History.TotalCount</c> reaches ⌈Cap × Multiplier⌉ they leave the pool
///     (per-student half-repeat threshold), with zero effect on weights otherwise.
/// </summary>
public sealed class DebtWeightRollCallAlgorithm(NewAlgorithmOptionsStore options) : IRollCallAlgorithm
{
    // Reuses the host's 内幕设置 attached-settings id so the existing per-student probability
    // control drives this algorithm's rigging without shipping another UI.
    private static readonly Guid s_behindSceneId = Guid.Parse(GlobalConstants.BehindSceneAttachedSettings);
    private static readonly Guid s_capId         = Guid.Parse(NewAlgorithmStudentAttachedSettings.AttachedSettingsId);

    // Label array is fixed as [group, gender]; the dimensions actually fed to the engine
    // follow the host's fair-draw toggles so settings keep working as users expect.
    private static readonly BalanceDimension s_groupDimension  = new(0);
    private static readonly BalanceDimension s_genderDimension = new(1);

    // 必中 (p = 100) cannot pre-allocate a slot through a weights-only interface; a weight this
    // large makes non-guaranteed candidates lose every draw position it participates in.
    private const double GuaranteedWeight = 1e9;

    private sealed record StudentLever(double Multiplier, int? BaseCap, bool Excluded, bool Guaranteed);

    public IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine                            engine,
        IReadOnlyList<Student>                eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot                fairSettings,
        string                                courseName)
    {
        var poolSize = eligibleCandidates.Count;
        if (poolSize == 0)
            return [];

        var opts = options.Current;
        var settings = new WeightSettings
        {
            PersonalHorizonRounds = opts.PersonalHorizonRounds,
            RandomFloor           = opts.RandomFloor,
            Dimensions            = BuildDimensions(fairSettings, opts.DimensionHorizonPerPick)
        };

        // Pass 1: resolve levers. Multiplier-rigged students stay in the pool (their share enters
        // the debt engine); only excluded/guaranteed/saturated students leave it.
        var levers             = new StudentLever?[poolSize];
        var cycleCounts        = new int[poolSize];
        var fairOrdinalByIndex = new int[poolSize];
        var fairIndexes        = new List<int>(poolSize);
        for (var i = 0; i < poolSize; i++)
        {
            var student = eligibleCandidates[i];
            cycleCounts[i]        = history.TryGetValue(student, out var record) ? Math.Max(0, record.TotalCount) : 0;
            fairOrdinalByIndex[i] = -1;

            var lever = ResolveLever(student);
            levers[i] = lever;
            if (lever.Excluded || lever.Guaranteed)
                continue;

            // 生效上限 = ⌈基础 Cap × 倍率⌉：倍率管长期频率，Cap 只做防失控阀门，两者正交
            if (lever.BaseCap is { } baseCap && cycleCounts[i] >= Math.Max(1, Math.Ceiling(baseCap * lever.Multiplier)))
                continue;

            fairOrdinalByIndex[i] = fairIndexes.Count;
            fairIndexes.Add(i);
        }

        // Pass 2: compute fair weights over the remaining pool only.
        var      fairCount  = fairIndexes.Count;
        var      fairLabels = BuildLabelAxes(eligibleCandidates, fairIndexes);
        double[] fairProbabilities;
        if (fairCount == 0)
        {
            fairProbabilities = [];
        }
        else
        {
            var pool      = new StudentMetaData[fairCount];
            var histories = new DrawHistory[fairCount];
            for (var f = 0; f < fairCount; f++)
            {
                var index = fairIndexes[f];
                // Cap is caller-side filter metadata for the engine; the debt engine only reads
                // Multiplier (share = Multiplier / ΣMultiplier). Unrigged students stay at 1.0.
                pool[f] = new StudentMetaData(f, 1, levers[index]!.Multiplier,
                    [fairLabels.Group[f], fairLabels.Gender[f]]);
                histories[f] = new DrawHistory(f, cycleCounts[index]);
            }

            try
            {
                var result = FairDrawWeights.Compute(pool, histories, settings, 1);
                fairProbabilities = FairDrawWeights.ToProbabilities(result, settings);
            }
            catch (ArgumentException)
            {
                // The constructed inputs cannot trip Compute's guards (multiplier > 0, counts >= 0,
                // labels >= 0). Degrade to uniform rather than crashing the draw pipeline
                // (a dispatcher fault would auto-disable the plugin).
                fairProbabilities = Enumerable.Repeat(1.0 / fairCount, fairCount).ToArray();
            }
        }

        // Pass 3: assemble weights in original candidate order.
        var candidates        = new WeightedCandidate<Student>[poolSize];
        var averageFairWeight = fairCount > 0 ? fairProbabilities.Sum() / fairCount : 1.0 / poolSize;
        for (var i = 0; i < poolSize; i++)
        {
            var lever = levers[i]!;
            if (lever.Excluded)
            {
                candidates[i] = new WeightedCandidate<Student> { Candidate = eligibleCandidates[i], Weight = 0.0 };
                continue;
            }

            if (lever.Guaranteed)
            {
                candidates[i] = new WeightedCandidate<Student>
                    { Candidate = eligibleCandidates[i], Weight = GuaranteedWeight };
                continue;
            }

            var f = fairOrdinalByIndex[i];
            if (f < 0)
            {
                // 个人半重复：抽满生效上限，移出候选池
                candidates[i] = new WeightedCandidate<Student> { Candidate = eligibleCandidates[i], Weight = 0.0 };
                continue;
            }

            var weight = fairProbabilities[f];
            candidates[i] = new WeightedCandidate<Student>
            {
                Candidate = eligibleCandidates[i],
                Weight = double.IsNaN(weight) || double.IsInfinity(weight)
                    ? averageFairWeight
                    : weight
            };
        }

        return candidates;
    }

    /// <summary>
    ///     Reads the host 内幕设置 (0 = exclude, 100 = 必中, otherwise the value is the long-term
    ///     share multiplier) and the plugin's base cap attachment （未启用 = 无个人上限）.
    /// </summary>
    private static StudentLever ResolveLever(Student student)
    {
        var scene      = student.GetAttachedObject<BehindSceneAttachedSettings>(s_behindSceneId);
        var multiplier = 1.0;
        var excluded   = false;
        var guaranteed = false;
        if (scene is { IsAttachSettingsEnabled: true })
        {
            var probability = Math.Clamp(scene.Probability, 0, 100);
            if (probability <= 0)
                excluded = true;
            else if (probability >= 100)
                guaranteed = true;
            else
                multiplier = probability;
        }

        var  capSettings = student.GetAttachedObject<NewAlgorithmStudentAttachedSettings>(s_capId);
        int? baseCap     = null;
        if (capSettings is { IsAttachSettingsEnabled: true })
        {
            var cap = capSettings.BaseCap;
            baseCap = double.IsNaN(cap) || double.IsInfinity(cap)
                ? 1
                : Math.Clamp((int)Math.Round(cap), 1, 1000);
        }

        return new StudentLever(multiplier, baseCap, excluded, guaranteed);
    }

    private static BalanceDimension[] BuildDimensions(FairDrawPolicySnapshot fairSettings, double horizonPerPick)
    {
        var dimensions = new List<BalanceDimension>(2);
        if (fairSettings.FairDrawGroup)
            dimensions.Add(s_groupDimension with { HorizonPerPick = horizonPerPick });
        if (fairSettings.FairDrawGender)
            dimensions.Add(s_genderDimension with { HorizonPerPick = horizonPerPick });
        return [.. dimensions];
    }

    /// <summary>
    ///     Maps group/gender strings to dense non-negative label indexes over the fair pool.
    ///     Blank values share one "unset" bucket so the debt math still balances them as a cohort.
    /// </summary>
    private static (int[] Group, int[] Gender) BuildLabelAxes(
        IReadOnlyList<Student> students,
        IReadOnlyList<int>     fairIndexes)
    {
        return (
            BuildLabelAxis(students, fairIndexes, static student => student.Group),
            BuildLabelAxis(students, fairIndexes, static student => student.Gender));
    }

    private static int[] BuildLabelAxis(
        IReadOnlyList<Student> students,
        IReadOnlyList<int>     fairIndexes,
        Func<Student, string>  selector)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels  = new int[fairIndexes.Count];
        for (var f = 0; f < fairIndexes.Count; f++)
        {
            var key = selector(students[fairIndexes[f]]) ?? string.Empty;
            if (!indexes.TryGetValue(key, out var label))
            {
                label = indexes.Count;
                indexes.Add(key, label);
            }

            labels[f] = label;
        }

        return labels;
    }
}
