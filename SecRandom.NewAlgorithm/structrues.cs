namespace SecRandom.NewAlgorithm;

/// <summary>
/// 学生的静态属性。
/// </summary>
/// <param name="Id">
/// 学生编号/标识
/// </param>
/// <param name="Cap">
/// 本周期内最多被抽中几次（可以用于暗改爆率）
/// </param>
/// <param name="Labels">
/// 需要做均衡的分类维度，用整数。例如：
/// [1,2,3] 代表在维度0属于1，在第维度1属于2，在第维度2属于3
/// </param>
public sealed record StudentMetaData(int Id, int Cap, int[] Labels)
{
    public StudentMetaData(int id, int cap) : this(Id: id, Cap: cap, []) { }
}

/// <summary>
/// 给某个维度的均衡值配置
/// </summary>
/// <param name="Dimension">
/// 维度编号，对应<see cref="StudentMetaData.Labels"/>的下标
/// </param>
/// <param name="HorizonPerPick">
/// 视野系数：乘以批量后得到该层的宽容度。
/// </param>
public readonly record struct BalanceDimension(int Dimension, double HorizonPerPick = 0.8);

/// <summary>某个学生在本周期内的历史统计。</summary>
/// <param name="Id">学生编号/标识</param>
/// <param name="DrawCount">
/// 本周期内已被抽中的次数。
/// </param>
public sealed record DrawHistory(int Id, int DrawCount);

public sealed record WeightSettings
{
    /// <summary>
    /// 个人视野 = 该值 × 池内人数。系统假装未来还要再抽这么多次来算每人应得份额。
    /// 小 → 谁超支就压很久, 接近点名册; 大 → 允许运气波动, 接近纯随机。
    /// 个人偏离的稳态方差约等于 该值 / (2(1 − RandomFloor)), 硬上界为该值。
    /// </summary>
    public double PersonalHorizonRounds { get; init; } = 2.0;

    /// <summary>
    /// 保底份额。任何池内学生的最低被抽概率 = 该值 / 池内人数。
    /// 它也是交替率的精确调节器: P(A) = 1/2 + (1−e)² / (2(2−e)), 误差 ±0.002。
    /// 注意池很小时保底会变大 (剩 10 人时每人 1%), 此时权重的影响被稀释。
    /// </summary>
    public double RandomFloor { get; init; } = 0.10;

    /// <summary>
    /// 需要做均衡的维度。空数组 = 只做个人层均衡。
    /// </summary>
    public BalanceDimension[] Dimensions { get; init; } = [];
}


/// <summary>
/// 单个学生的权重附加中间量
/// </summary>
/// <param name="Id">学生编号/标识</param>
/// <param name="Weight">最终采样权重（未归一化）</param>
/// <param name="PersonalDebt">个人欠账: 到未来 H 次为止应得的份额减去已拿到的, 负数归零。</param>
/// <param name="DimensionDebts">各维度欠账, 与 <see cref="WeightSettings.Dimensions"/> 同序。</param>
public sealed record CandidateWeight(
    int      Id,
    double   Weight,
    double   PersonalDebt,
    double[] DimensionDebts
);

/// <param name="Candidates">各学生的最终权重与中间量</param>
/// <param name="WeightSum">权重合计；退化时等于池内人数</param>
/// <param name="DegradedToUniform">
/// true 表示全池欠账同时清零, 已退化为等权。
/// </param>
/// <param name="DeterministicPick">
/// true 表示池内只剩一人, 抽取结果确定。
/// </param>
public sealed record WeightResult(
    IReadOnlyList<CandidateWeight> Candidates,
    double                         WeightSum,
    bool                           DegradedToUniform,
    bool                           DeterministicPick
);
