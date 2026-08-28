using SecRandom.NewAlgorithm;

namespace SecRandom.Sim;

public sealed record SimulationResult(
    SimulationConfig               Config,
    IReadOnlyList<StudentMetaData> Students,
    IReadOnlyList<DrawLogEntry>    Entries,
    TimeSpan                       Elapsed)
{
    /// <summary>每周期应抽总数 = Σ⌈Cap × 倍率ᵢ⌉。</summary>
    public long DrawsPerCycle => Config.DrawsPerCycle();

    public int TotalDraws => Entries.Count;
}

/// <summary>
/// 周期制仿真驱动器: 模拟宿主的调用方式 ——
/// 每个周期重置计数与满池; 每次抽取调用一次 <see cref="FairDrawWeights.Compute"/>;
/// 命中生效上限 (⌈Cap × 倍率⌉) 的学生立即离池; 池抽空即周期结束。
/// </summary>
public static class SimDriver
{
    public static SimulationResult Run(SimulationConfig config, IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var settings = config.BuildWeightSettings();
        var students = config.BuildStudents();
        var n        = students.Length;
        var caps     = students.Select(s => s.Cap).ToArray();
        var dpc      = caps.Select(c => (long)c).Sum();
        var rng      = new Random(config.Seed);
        var capacity = config.Cycles * dpc;
        var entries  = new List<DrawLogEntry>((int)Math.Min(capacity, int.MaxValue));
        var counts   = new int[n]; // 本周期内每人已抽次数, 周期开始清零
        var started  = System.Diagnostics.Stopwatch.StartNew();

        // 活性保护: 权重全零 + RandomFloor=0 时算法可能抽不干池子,
        // 超过宽松上限直接抛异常, 把算法缺陷变成可见错误而不是挂死仿真器。
        var livelockBudget = Math.Max(capacity * 64, 1_000_000);

        var globalIndex = 0;
        for (var cycle = 0; cycle < config.Cycles; cycle++)
        {
            Array.Clear(counts);
            var pool      = new List<StudentMetaData>(students);
            var histories = new List<DrawHistory>(n);

            var drawIndexInCycle = 0;
            var cycleDraws       = 0;
            while (pool.Count > 0)
            {
                if (cycleDraws > livelockBudget)
                    throw new InvalidOperationException(
                        $"周期 {cycle} 已抽 {cycleDraws} 次仍未抽空 (预期 {dpc}); " +
                        "疑似权重恒零导致的活性死锁, 检查 PersonalHorizonRounds / RandomFloor 组合");
                var batch = Math.Min(config.BatchSize, pool.Count);
                // 同批次内不放回: 抽中即移出批次池, 未达 Cap 者下一批才能再次出现
                // (与 SecRandom 现有 WeightedDrawEngine 的每批无重复契约一致)
                var batchPool = new List<StudentMetaData>(pool);
                for (var slot = 0; slot < batch && batchPool.Count > 0; slot++)
                {
                    // 每个名额重算; 批量参数钳到当前池大小, 否则 Compute 校验会抛
                    var batchArg = Math.Min(batch, batchPool.Count);
                    histories.Clear();
                    foreach (var student in batchPool)
                        histories.Add(new DrawHistory(student.Id, counts[student.Id]));
                    var result = FairDrawWeights.Compute(batchPool, histories, settings, batchArg);
                    var probs  = FairDrawWeights.ToProbabilities(result, settings);
                    var pick   = Sample(rng, probs);

                    var picked = result.Candidates[pick];
                    var maxW   = 0.0;
                    foreach (var candidate in result.Candidates)
                        maxW = Math.Max(maxW, candidate.Weight);

                    entries.Add(new DrawLogEntry(
                        cycle,
                        drawIndexInCycle,
                        globalIndex,
                        picked.Id,
                        batchPool.Count,
                        picked.Weight >= maxW,
                        result.DegradedToUniform,
                        slot));
                    globalIndex++;
                    drawIndexInCycle++;
                    cycleDraws++;

                    counts[picked.Id]++;
                    if (counts[picked.Id] >= caps[picked.Id])
                        pool.RemoveAt(pool.FindIndex(s => s.Id == picked.Id));
                    batchPool.RemoveAt(pick);
                }
            }

            progress?.Report(cycle + 1);
        }

        started.Stop();
        return new SimulationResult(config, students, entries, started.Elapsed);
    }

    /// <summary>按 (1−floor)·w/Σw + floor/n 概率逆累积抽样。</summary>
    private static int Sample(Random rng, double[] probabilities)
    {
        var r       = rng.NextDouble();
        var running = 0.0;
        for (var i = 0; i < probabilities.Length - 1; i++)
        {
            running += probabilities[i];
            if (r < running)
                return i;
        }

        // 浮点下概率和可能略小于 1, 此时落到最后一项
        return probabilities.Length - 1;
    }
}
