using System.Globalization;
using SecRandom.NewAlgorithm;

namespace SecRandom.Sim;

public sealed record HardCheck(string Name, bool Passed, string Expected, string Actual);

public sealed record StatEntry(string Name, string Value);

public sealed record SimMetrics(
    IReadOnlyList<HardCheck> Hard,
    IReadOnlyList<StatEntry> Stats)
{
    public bool AllHardPassed => Hard.All(check => check.Passed);
}

public static class MetricsCalculator
{
    public static SimMetrics Compute(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var  config    = result.Config;
        var  entries   = result.Entries;
        int  n         = config.StudentCount;
        int  cycles    = config.Cycles;
        var  caps      = result.Students.Select(s => s.Cap).ToArray();   // 生效 Cap = ⌈Cap × 倍率⌉
        long dpc       = caps.Select(c => (long)c).Sum();                // 每周期应抽总数
        var  perCycle  = new int[cycles, n];            // 周期×学生计数
        var  cycleRows = new int[cycles];
        long degraded  = 0;
        long argmax    = 0;
        long argmaxLean = 0;   // 排除降级与单人池
        long leanCount  = 0;
        bool indexOk   = true;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.GlobalIndex != i)
                indexOk = false;
            perCycle[e.CycleIndex, e.PickedId]++;
            cycleRows[e.CycleIndex]++;
            if (e.Degraded) degraded++;
            if (e.WasArgmax) argmax++;
            bool lean = !e.Degraded && e.PoolSize > 1;
            if (lean)
            {
                leanCount++;
                if (e.WasArgmax) argmaxLean++;
            }
        }

        int minDelta = int.MaxValue, maxDelta = int.MinValue;   // 实测 − 生效Cap 的偏差
        bool noOverCap = true;
        for (int c = 0; c < cycles; c++)
        for (int s = 0; s < n; s++)
        {
            int delta = perCycle[c, s] - caps[s];
            minDelta  = Math.Min(minDelta, delta);
            maxDelta  = Math.Max(maxDelta, delta);
            if (delta > 0) noOverCap = false;
        }
        if (entries.Count == 0) { minDelta = 0; maxDelta = 0; }

        bool capExact   = minDelta == 0 && maxDelta == 0;
        bool totalsOk   = cycleRows.All(row => row == dpc);
        bool batchDistinct = CheckBatchDistinct(entries);

        var hard = new List<HardCheck>
        {
            new("每人每周期被抽次数 == 生效 Cap (⌈Cap×倍率⌉)",
                capExact,
                Expected: "每人恰好本人生效 Cap 次",
                Actual:   $"偏差 min={minDelta}, max={maxDelta}"),
            new("无人超过生效 Cap",
                noOverCap,
                Expected: "偏差 max ≤ 0",
                Actual:   $"偏差 max={maxDelta}"),
            new("每周期抽取总数 == Σ生效 Cap",
                totalsOk,
                Expected: $"{dpc}",
                Actual:   cycleRows.Length == 0 ? "(无数据)"
                          : $"min={cycleRows.Min()}, max={cycleRows.Max()}"),
            new("GlobalIndex 从 0 连续递增",
                indexOk,
                Expected: "0..N-1 无断裂",
                Actual:   indexOk ? "连续" : "存在断裂"),
            new("同批次内无重复中选人",
                batchDistinct,
                Expected: "批次内 PickedId 互不相同",
                Actual:   batchDistinct ? "无重复" : "存在重复"),
        };

        var stats = new List<StatEntry>
        {
            new("总抽取数", entries.Count.ToString(CultureInfo.InvariantCulture)),
            new("耗时", $"{result.Elapsed.TotalMilliseconds:F1} ms"),
            new("Degraded 比例", Ratio(degraded, entries.Count)),
            new("WasArgmax (全部)", Ratio(argmax, entries.Count)),
            new("WasArgmax (排降级+单人池)", leanCount == 0 ? "n/a" : Ratio(argmaxLean, leanCount)),
        };
        AddBehaviorStats(result, stats, perCycle);
        return new SimMetrics(hard, stats);
    }

    private static void AddBehaviorStats(
        SimulationResult result, List<StatEntry> stats, int[,] perCycle)
    {
        var entries = result.Entries;
        var config  = result.Config;
        int n = config.StudentCount;

        // 相邻同人率: 同周期内连续两次抽中同一人 (纯随机基线 ≈ 1/N)
        long adjacent = 0, adjacentTotal = 0;
        for (int i = 1; i < entries.Count; i++)
        {
            if (entries[i].CycleIndex != entries[i - 1].CycleIndex) continue;
            adjacentTotal++;
            if (entries[i].PickedId == entries[i - 1].PickedId) adjacent++;
        }
        stats.Add(new StatEntry("相邻同人率", adjacentTotal == 0 ? "n/a" : Ratio(adjacent, adjacentTotal)));
        // 相邻异性别率 (性别交替率 P(A)): 相邻两抽组别不同的比例, 全局依次计算。
        // 期望出自 RandomFloor 的交替率公式 P(A) = 1/2 + (1−f)² / (2(2−f))
        if (config.GenderGroupSizes.Length > 1)
        {
            var genderOf = result.Students.ToDictionary(s => s.Id, s => s.Labels[0]);
            long cross = 0, crossTotal = 0;
            for (int i = 1; i < entries.Count; i++)
            {
                crossTotal++;
                if (genderOf[entries[i].PickedId] != genderOf[entries[i - 1].PickedId]) cross++;
            }
            if (crossTotal > 0)
            {
                double floor = config.RandomFloor;
                double expectedPA = 0.5 + (1 - floor) * (1 - floor) / (2 * (2 - floor));
                stats.Add(new StatEntry("相邻异性别率 (期望≈" + expectedPA.ToString("P1", CultureInfo.InvariantCulture) + ")",
                    Ratio(cross, crossTotal)));
            }
        }

        // 同一人两次被抽的间隔, 跨周期按全局序号计算 (学生感知不分周期),
        // 分位数比 min/max 更有区分度 (min 恒为 1); 另附精确刺激度指标 P(间隔=1)
        {
            var gaps = new List<double>(entries.Count);
            var lastSeen = new int[n];
            Array.Fill(lastSeen, -1);
            foreach (var e in entries)
            {
                if (lastSeen[e.PickedId] >= 0)
                    gaps.Add(e.GlobalIndex - lastSeen[e.PickedId]);
                lastSeen[e.PickedId] = e.GlobalIndex;
            }
            if (gaps.Count > 0)
            {
                gaps.Sort();
                long ones = gaps.Count(gap => gap <= 1);
                stats.Add(new StatEntry("同人再抽间隔 p50/p95/max (跨周期)",
                    $"{gaps[(int)((gaps.Count - 1) * 0.50)]:F0} / {gaps[(int)((gaps.Count - 1) * 0.95)]:F0} / {gaps[^1]:F0}"));
                stats.Add(new StatEntry("P(间隔 = 1) (跨周期)", Ratio(ones, gaps.Count)));
            }
        }

        // 性别组占比: 累计实际占比 vs 卡占比的最大偏差
        int groups = config.GenderGroupSizes.Length;
        if (groups > 1)
        {
            int[] groupIds = new int[n];
            foreach (var s in result.Students) groupIds[s.Id] = s.Labels[0];
            // 期望占比 = 该组倍率之和 ÷ 全池倍率 (覆盖后的真实长期目标份额)
            double multiplierTotal = result.Students.Sum(s => s.Multiplier);
            var expected = new double[groups];
            for (int s = 0; s < n; s++)
                expected[groupIds[s]] += result.Students[s].Multiplier / multiplierTotal;
            var cum = new long[groups];
            double worst = 0.0; int worstG = -1; long worstAt = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                cum[groupIds[entries[i].PickedId]]++;
                long total = i + 1;
                if (total < config.DrawsPerCycle()) continue;   // 首个周期样本太少, 偏差全是噪声
                for (int g = 0; g < groups; g++)
                {
                    double dev = Math.Abs((double)cum[g] / total - expected[g]);
                    if (dev > worst) { worst = dev; worstG = g; worstAt = total; }
                }
            }
            stats.Add(new StatEntry("组占比最大偏差 (首周期后)",
                $"组{worstG} 偏差 {worst:P2} (第 {worstAt} 抽, 期望 {expected[worstG]:P2})"));
        }
    }

    private static bool CheckBatchDistinct(IReadOnlyList<DrawLogEntry> entries)
    {
        // 连续条目中 BatchSlot 从 0 递增的一段构成一个批次
        var seen = new HashSet<int>();
        int prevSlot = -1;
        foreach (var e in entries)
        {
            if (e.BatchSlot <= prevSlot) seen.Clear();
            if (!seen.Add(e.PickedId)) return false;
            prevSlot = e.BatchSlot;
        }
        return true;
    }

    private static string Ratio(long part, long total)
        => total == 0 ? "n/a" : $"{part}/{total} = {(double)part / total:P1}";
}
