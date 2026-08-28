using SecRandom.NewAlgorithm;

namespace SecRandom.Sim;

/// <summary>
/// 一次仿真的全部输入。纯数据, 可按需序列化。
/// </summary>
public sealed record SimulationConfig
{
    /// <summary>学生总数 (Id 为 0..N-1)。</summary>
    public int StudentCount { get; init; } = 40;

    /// <summary>每人每周期的抽取上限。</summary>
    public int Cap { get; init; } = 1;

    /// <summary>仿真周期数。</summary>
    public int Cycles { get; init; } = 10;

    /// <summary>每次点击抽几人 (每名额重算权重)。</summary>
    public int BatchSize { get; init; } = 1;

    /// <summary>固定种子; 同参数同种子结果完全可复现。</summary>
    public int Seed { get; init; } = 1;

    public double PersonalHorizonRounds { get; init; } = 2.0;

    /// <summary>性别维度 (维度 0) 的 HorizonPerPick。</summary>
    public double GenderHorizonPerPick { get; init; } = 0.8;

    public double RandomFloor { get; init; } = 0.10;

    /// <summary>
    /// 性别维度 (维度 0) 各组人数, 总和必须等于 <see cref="StudentCount"/>。
    /// 单元素数组 = 不做性别均衡。
    /// </summary>
    public int[] GenderGroupSizes { get; init; } = [20, 20];

    /// <summary>
    /// 逐人倍率覆盖 (Id → 爆率倍率)；未覆盖的学生 Multiplier = 1.0。
    /// 倍率通过 share = Multiplier ÷ ΣMultiplier 长期精确生效。
    /// </summary>
    public IReadOnlyDictionary<int, double> MultiplierOverrides { get; init; } = new Dictionary<int, double>();

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(StudentCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Cap,          1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Cycles,       1);
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize,    1);
        if (RandomFloor is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(RandomFloor), "RandomFloor 须在 [0, 1] 内");
        if (PersonalHorizonRounds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(PersonalHorizonRounds));
        if (GenderHorizonPerPick < 0.0)
            throw new ArgumentOutOfRangeException(nameof(GenderHorizonPerPick));
        if (GenderGroupSizes.Length == 0)
            throw new ArgumentException("至少需要一个分组", nameof(GenderGroupSizes));
        var sum = 0;
        foreach (var size in GenderGroupSizes)
        {
            if (size < 0)
                throw new ArgumentException("分组人数不能为负", nameof(GenderGroupSizes));
            sum += size;
        }

        if (sum != StudentCount)
            throw new ArgumentException(
                $"分组人数之和 {sum} 必须等于学生总数 {StudentCount}", nameof(GenderGroupSizes));

        foreach (var (id, multiplier) in MultiplierOverrides)
        {
            if (id < 0 || id >= StudentCount)
                throw new ArgumentException($"倍率覆盖的 Id {id} 超出 0..{StudentCount - 1}", nameof(MultiplierOverrides));
            if (multiplier <= 0.0 || double.IsNaN(multiplier) || double.IsInfinity(multiplier))
                throw new ArgumentException($"学生 {id} 的倍率 {multiplier} 必须为正的有限值", nameof(MultiplierOverrides));
        }
    }

    /// <summary>按分组连续分配 Id 与标签，应用逐人倍率覆盖与生效 Cap。</summary>
    public StudentMetaData[] BuildStudents()
    {
        var students = new StudentMetaData[StudentCount];
        var id       = 0;
        for (var group = 0; group < GenderGroupSizes.Length; group++)
        for (var i = 0; i < GenderGroupSizes[group]; i++, id++)
        {
            var multiplier = MultiplierOverrides.GetValueOrDefault(id, 1.0);
            students[id] = new StudentMetaData(id, EffectiveCap(multiplier), multiplier, [group]);
        }

        return students;
    }

    /// <summary>
    /// 生效 Cap = ⌈基础 Cap × 倍率⌉：倍率越高同期可抽次数上限同步放大，
    /// 两个参数因此正交（倍率管长期频率，Cap 只做防失控阀门）。
    /// </summary>
    public int EffectiveCap(double multiplier)
    {
        return Math.Max(1, (int)Math.Ceiling(Cap * multiplier));
    }

    /// <summary>每周期应抽总数 = Σ⌈Cap × 倍率ᵢ⌉。</summary>
    public long DrawsPerCycle()
    {
        long total = 0;
        for (var id = 0; id < StudentCount; id++)
            total += EffectiveCap(MultiplierOverrides.GetValueOrDefault(id, 1.0));
        return total;
    }

    public WeightSettings BuildWeightSettings()
    {
        return new WeightSettings
        {
            PersonalHorizonRounds = PersonalHorizonRounds,
            RandomFloor           = RandomFloor,
            // 只有一个组时维度均衡无意义, 直接关闭
            Dimensions = GenderGroupSizes.Length > 1
                ? [new BalanceDimension(0, GenderHorizonPerPick)]
                : []
        };
    }
}
