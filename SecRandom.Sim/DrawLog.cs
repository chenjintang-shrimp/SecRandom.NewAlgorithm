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

    /// <summary>导入名单时的表头, 在 PickedId 后追加 PickedName 列。</summary>
    public const string CsvHeaderWithName =
        "CycleIndex,DrawIndexInCycle,GlobalIndex,PickedId,PickedName,PoolSize,WasArgmax,Degraded,BatchSlot";

    public static void WriteCsv(TextWriter writer, IReadOnlyList<DrawLogEntry> entries,
        IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        writer.WriteLine(names is null ? CsvHeader : CsvHeaderWithName);
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
            if (names is not null)
                WriteCsvField(writer, names[e.PickedId]);
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

    public static void WriteCsvFile(string path, IReadOnlyList<DrawLogEntry> entries,
        IReadOnlyList<string>? names = null)
    {
        using var writer = new StreamWriter(path, false);
        WriteCsv(writer, entries, names);
    }

    /// <summary>极简 CSV 转义: 含 , " 或换行才加引号, 引号双写; 否则原样写出。字段后恒补逗号。</summary>
    private static void WriteCsvField(TextWriter writer, string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            writer.Write(value);
            writer.Write(',');
            return;
        }

        writer.Write('"');
        foreach (var c in value)
        {
            if (c == '"') writer.Write('"');
            writer.Write(c);
        }

        writer.Write("\",");
    }
}
