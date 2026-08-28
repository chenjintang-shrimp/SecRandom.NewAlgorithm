namespace SecRandom.NewAlgorithm.Plugin;

/// <summary>
///     Tunable parameters of the share-debt weighting engine, surfaced in the plugin settings
///     page and persisted to <c>options.json</c> in the plugin config folder.
/// </summary>
public sealed class NewAlgorithmOptions
{
    /// <summary>个人视野轮数：个人欠账的时间尺度（该值 × 池内人数），默认 2.0</summary>
    public double PersonalHorizonRounds { get; set; } = 2.0;

    /// <summary>随机保底份额：任何学生的最低被抽概率 = 该值 ÷ 池内人数，默认 0.10</summary>
    public double RandomFloor { get; set; } = 0.10;

    /// <summary>维度视野系数：分组/性别欠账的宽容度（该值 × 批量），默认 0.8</summary>
    public double DimensionHorizonPerPick { get; set; } = 0.8;
}
