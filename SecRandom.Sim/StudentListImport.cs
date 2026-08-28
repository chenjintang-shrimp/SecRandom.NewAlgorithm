using System.Text.Json;

namespace SecRandom.Sim;

/// <summary>
/// 从 SecRandom 名单导出 JSON 还原的一名学生。
/// </summary>
/// <param name="Id">仿真用连续 Id (按性别归组重排, 与源文件顺序不同)。</param>
/// <param name="Name">学生姓名。</param>
/// <param name="Gender">性别原文 (如 男/女); 空值归为 "未标注"。</param>
/// <param name="Group">小组原文 (如 第三小组); 仅展示用, 暂不进均衡维度。</param>
/// <param name="SourceId">导出文件里的原始 id 字段。</param>
public sealed record ImportedStudent(int Id, string Name, string Gender, string Group, string SourceId);

/// <summary>
/// 一份导入名单的仿真就绪形态: Id 已按性别首次出现顺序归组连续化,
/// 因此 <see cref="GenderGroupSizes"/> 可直接喂给 SimulationConfig.GenderGroupSizes。
/// </summary>
/// <param name="Students">按新 Id 排序的学生。</param>
/// <param name="GenderLabels">分组标签, 首次出现顺序 (与 GenderGroupSizes 同序)。</param>
/// <param name="GenderGroupSizes">各组人数。</param>
/// <param name="Names">Id → 显示名 (与 Students 同序, 冗余字段便于直接给配置)。</param>
public sealed record ImportedRoster(
    IReadOnlyList<ImportedStudent> Students,
    IReadOnlyList<string>          GenderLabels,
    int[]                          GenderGroupSizes,
    IReadOnlyList<string>          Names);

/// <summary>
/// SecRandom 名单导出 JSON → 仿真名单。只读 students[] 的
/// exists / gender / group / id / name 五个字段, attached_objects 等全部忽略。
/// exists == false 的学生被过滤。
/// </summary>
public static class StudentListImporter
{
    public static ImportedRoster LoadFile(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static ImportedRoster Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("students", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("文件中找不到 students 数组, 不是有效的 SecRandom 名单导出");

        // gender 首次出现顺序决定分组编号与 Id 区间, 保证同组标签连续 (仿真硬约束)
        var genders = new List<string>();
        var raw     = new List<(string Name, string Gender, string Group, string SourceId)>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.False)
                continue;
            var sourceId = el.TryGetProperty("id", out var idEl) ? idEl.ToString() : "";
            var name     = ReadString(el, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrEmpty(sourceId) ? $"#{raw.Count + 1}" : $"#{sourceId}";
            var gender = ReadString(el, "gender");
            if (string.IsNullOrWhiteSpace(gender))
                gender = "未标注";
            var group = ReadString(el, "group");
            if (!genders.Contains(gender))
                genders.Add(gender);
            raw.Add((name, gender, group, sourceId));
        }

        if (raw.Count == 0)
            throw new InvalidDataException("students 数组为空 (或全部被 exists=false 过滤)");

        // OrderBy 稳定: 组内保持源文件相对顺序。g 极小, IndexOf/Count 的 O(n·g) 无所谓。
        var ordered  = raw.OrderBy(s => genders.IndexOf(s.Gender)).ToList();
        var students = ordered
                       .Select((s, id) => new ImportedStudent(id, s.Name, s.Gender, s.Group, s.SourceId))
                       .ToList();
        var sizes = genders.Select(g => ordered.Count(s => s.Gender == g)).ToArray();
        return new ImportedRoster(students, genders, sizes, ordered.Select(s => s.Name).ToList());
    }

    private static string ReadString(JsonElement el, string property)
    {
        return el.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
