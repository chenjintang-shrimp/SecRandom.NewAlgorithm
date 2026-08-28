namespace SecRandom.Sim;

/// <summary>
/// 单次抽取的完整审计记录。
/// PoolSize / WasArgmax / Degraded 必须在抽取时实时记录, 事后无法从 ID 序列重建。
/// </summary>
/// <param name="CycleIndex">周期下标, 从 0 开始。</param>
/// <param name="DrawIndexInCycle">该周期内第几次抽取 (含批量中的每个名额), 从 0 开始。</param>
/// <param name="GlobalIndex">全局抽取序号, 从 0 开始连续递增。</param>
/// <param name="PickedId">被抽中的学生 Id。</param>
/// <param name="PoolSize">本次抽取发生前的候选人数量。</param>
/// <param name="WasArgmax">被抽中者是否是当时权重最大者 (平权时取并列也算)。</param>
/// <param name="Degraded">本次抽取权重是否已退化为均匀。</param>
/// <param name="BatchSlot">本次抽取在同一批次内的名额下标, 单抽恒为 0。</param>
public readonly record struct DrawLogEntry(
    int  CycleIndex,
    int  DrawIndexInCycle,
    int  GlobalIndex,
    int  PickedId,
    int  PoolSize,
    bool WasArgmax,
    bool Degraded,
    int  BatchSlot
);

public static class DrawLog
{
    public const string CsvHeader =
        "CycleIndex,DrawIndexInCycle,GlobalIndex,PickedId,PoolSize,WasArgmax,Degraded,BatchSlot";

    public static void WriteCsv(TextWriter writer, IReadOnlyList<DrawLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        writer.WriteLine(CsvHeader);
        foreach (var e in entries)
        {
            writer.Write(e.CycleIndex);
            writer.Write(',');
            writer.Write(e.DrawIndexInCycle);
            writer.Write(',');
            writer.Write(e.GlobalIndex);
            writer.Write(',');
            writer.Write(e.PickedId);
            writer.Write(',');
            writer.Write(e.PoolSize);
            writer.Write(',');
            writer.Write(e.WasArgmax ? "True" : "False");
            writer.Write(',');
            writer.Write(e.Degraded ? "True" : "False");
            writer.Write(',');
            writer.Write(e.BatchSlot);
            writer.Write('\n');
        }
    }

    public static void WriteCsvFile(string path, IReadOnlyList<DrawLogEntry> entries)
    {
        using var writer = new StreamWriter(path, false);
        WriteCsv(writer, entries);
    }
}
