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
///     roll-call pipeline. The contract only allows emitting weighted candidates; winner
///     selection stays in the host's <see cref="WeightedDrawEngine{TCandidate}" /> and the
///     draw commit/verification pipeline is untouched.
///     Per-student rigging reuses the host's built-in 内幕设置 (BehindScene) attached settings:
///     0 excludes the student from the pool, 100 means 必中, intermediate values scale the
///     weight to p × the average fair weight.
/// </summary>
public sealed class DebtWeightRollCallAlgorithm(NewAlgorithmOptionsStore options) : IRollCallAlgorithm
{
    // Reuses the host's 内幕设置 attached-settings id so the existing per-student/probability
    // control drives this algorithm's rigging without shipping another UI.
    private static readonly Guid s_behindSceneId = Guid.Parse(GlobalConstants.BehindSceneAttachedSettings);
    private static readonly Guid s_capId = Guid.Parse(NewAlgorithmStudentAttachedSettings.AttachedSettingsId);

    // Label array is fixed as [group, gender]; the dimensions actually fed to the engine
    // follow the host's fair-draw toggles so settings keep working as users expect.
    private static readonly BalanceDimension s_groupDimension = new(Dimension: 0);
    private static readonly BalanceDimension s_genderDimension = new(Dimension: 1);

    // 必中 (p = 100) cannot pre-allocate a slot through a weights-only interface; a weight this
    // large makes non-guaranteed candidates lose every draw position it participates in.
    private const double GuaranteedWeight = 1e9;

    public IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine engine,
        IReadOnlyList<Student> eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot fairSettings,
        string courseName)
    {
        var poolSize = eligibleCandidates.Count;
        if (poolSize == 0)
            return [];

        var opts = options.Current;
        var settings = new WeightSettings
        {
            PersonalHorizonRounds = opts.PersonalHorizonRounds,
            RandomFloor = opts.RandomFloor,
            Dimensions = BuildDimensions(fairSettings, opts.DimensionHorizonPerPick)
        };

        // Pass 1: resolve per-student levers. Rigged students (enabled 内幕设置) leave the fair
        // pool entirely; Cap doubles as a per-student half-repeat threshold measured against the
        // same History.TotalCount the engine's repeat filter uses — a student at their cap is
        // saturated, leaves the pool, and gets weight 0 (shares rebalance to everyone else).
        var rig = new BehindSceneAttachedSettings?[poolSize];
        var caps = new int[poolSize];
        var cycleCounts = new int[poolSize];
        var fairOrdinalByIndex = new int[poolSize];
        var fairIndexes = new List<int>(poolSize);
        for (var i = 0; i < poolSize; i++)
        {
            var student = eligibleCandidates[i];
            cycleCounts[i] = history.TryGetValue(student, out var record) ? Math.Max(0, record.TotalCount) : 0;
            fairOrdinalByIndex[i] = -1;

            var scene = student.GetAttachedObject<BehindSceneAttachedSettings>(s_behindSceneId);
            if (scene is { IsAttachSettingsEnabled: true })
            {
                rig[i] = new BehindSceneAttachedSettings
                {
                    IsAttachSettingsEnabled = true,
                    Probability = Math.Clamp(scene.Probability, 0, 100)
                };
                continue;
            }

            var cap = GetConfiguredCap(student);
            if (cap is not null && cycleCounts[i] >= cap.Value)
                continue;

            caps[i] = cap ?? 1;
            fairOrdinalByIndex[i] = fairIndexes.Count;
            fairIndexes.Add(i);
        }

        // Pass 2: compute fair weights over the unrigged pool only.
        var fairCount = fairIndexes.Count;
        var fairLabels = BuildLabelAxes(eligibleCandidates, fairIndexes);
        double[] fairProbabilities;
        if (fairCount == 0)
        {
            fairProbabilities = [];
        }
        else
        {
            var pool = new StudentMetaData[fairCount];
            var histories = new DrawHistory[fairCount];
            for (var f = 0; f < fairCount; f++)
            {
                var index = fairIndexes[f];
                // share = Cap / ΣCap; students without the plugin's attached Cap stay at the
                // default equal share (Cap = 1).
                pool[f] = new StudentMetaData(f, caps[index], [fairLabels.Group[f], fairLabels.Gender[f]]);
                histories[f] = new DrawHistory(f, cycleCounts[index]);
            }

            try
            {
                var result = FairDrawWeights.Compute(pool, histories, settings, batchSize: 1);
                fairProbabilities = FairDrawWeights.ToProbabilities(result, settings);
            }
            catch (ArgumentException)
            {
                // The constructed inputs cannot trip Compute's guards (Cap > 0, counts >= 0,
                // labels >= 0). Degrade to uniform rather than crashing the draw pipeline
                // (a dispatcher fault would auto-disable the plugin).
                fairProbabilities = Enumerable.Repeat(1.0 / fairCount, fairCount).ToArray();
            }
        }

        // Pass 3: assemble weights in original candidate order. Rigged weights are measured
        // against the fair-pool average, so p reads as "p × the average student's pull";
        // cap-saturated students keep weight 0.
        var averageFairWeight = fairCount > 0 ? fairProbabilities.Sum() / fairCount : 1.0 / poolSize;
        var candidates = new WeightedCandidate<Student>[poolSize];
        for (var i = 0; i < poolSize; i++)
        {
            var scene = rig[i];
            if (scene is not null)
            {
                candidates[i] = new WeightedCandidate<Student>
                {
                    Candidate = eligibleCandidates[i],
                    Weight = scene.Probability <= 0
                        ? 0.0
                        : scene.Probability >= 100
                            ? GuaranteedWeight
                            : scene.Probability * averageFairWeight
                };
                continue;
            }

            var f = fairOrdinalByIndex[i];
            if (f < 0)
            {
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
    ///     The configured per-student cap （欠账份额上限 / 个人抽取次数上限）, or null when the
    ///     attached setting is disabled. Corrupted values fall back to the equal-share default.
    /// </summary>
    private static int? GetConfiguredCap(Student student)
    {
        var settings = student.GetAttachedObject<NewAlgorithmStudentAttachedSettings>(s_capId);
        if (settings is not { IsAttachSettingsEnabled: true })
            return null;

        var cap = settings.ShareCap;
        if (double.IsNaN(cap) || double.IsInfinity(cap))
            return 1;

        return Math.Clamp((int)Math.Round(cap), 1, 1000);
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
        IReadOnlyList<int> fairIndexes)
    {
        return (
            BuildLabelAxis(students, fairIndexes, static student => student.Group),
            BuildLabelAxis(students, fairIndexes, static student => student.Gender));
    }

    private static int[] BuildLabelAxis(
        IReadOnlyList<Student> students,
        IReadOnlyList<int> fairIndexes,
        Func<Student, string> selector)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new int[fairIndexes.Count];
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
