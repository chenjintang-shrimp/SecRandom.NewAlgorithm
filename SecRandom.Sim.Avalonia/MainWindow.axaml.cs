using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScottPlot;
using SecRandom.Sim;

namespace SecRandom.Sim.Avalonia;

public partial class MainWindow : Window
{
    private SimulationResult? _result;
    private int[]?            _lastGroupOf;

    private static readonly ScottPlot.Color[] Palette =
    [
        ScottPlot.Color.FromHex("#4C8DDA"),
        ScottPlot.Color.FromHex("#E45756"),
        ScottPlot.Color.FromHex("#54A24B"),
        ScottPlot.Color.FromHex("#F28E2C"),
        ScottPlot.Color.FromHex("#B279A2"),
        ScottPlot.Color.FromHex("#76B7B2"),
        ScottPlot.Color.FromHex("#EDC948"),
        ScottPlot.Color.FromHex("#9D755D"),
    ];
    private static readonly ScottPlot.Color BoundaryColor = ScottPlot.Color.FromHex("#D98880");

    /// <summary>Timeline 点数预算; 超出即只画前若干周期 (见 TimelineNote)。</summary>
    private const int TimelinePointBudget = 150_000;

    static MainWindow()
    {
        SetupPlotFonts();
    }

    /// <summary>
    /// Skia/ScottPlot 的字体枚举看不到「仅当前用户安装」的字体 (Win11 用户字体目录),
    /// 直接按文件路径注册进 ScottPlot 的 resolver。优先 HarmonyOS Sans SC,
    /// 其次 Maple Mono NF CN (含 CJK 的 Maple Mono 变体; 原版 Maple Mono 无中文字形),
    /// 兜底系统雅黑。
    /// </summary>
    private static void SetupPlotFonts()
    {
        string userFonts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Fonts");
        string systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        string? Find(string file) =>
            new[] { Path.Combine(userFonts, file), Path.Combine(systemFonts, file) }
                .FirstOrDefault(File.Exists);

        // 注: name 必须取字体的内部 family 名, 并赋给 Fonts.Default
        (string File, string Family)[] candidates =
        [
            ("HarmonyOS_Sans_SC_Regular.ttf", "HarmonyOS Sans SC"),
            ("MapleMono-NF-CN-Regular.ttf",   "Maple Mono NF CN"),
        ];
        (string File, string Family)[] boldCandidates =
        [
            ("HarmonyOS_Sans_SC_Bold.ttf", "HarmonyOS Sans SC"),
            ("MapleMono-NF-CN-Bold.ttf",   "Maple Mono NF CN"),
        ];

        var regular = candidates
            .Select(c => (Path: Find(c.File), c.Family))
            .FirstOrDefault(c => c.Path is not null);
        if (regular.Path is null)
        {
            ScottPlot.Fonts.Default = "Microsoft YaHei";   // 系统枚举兜底
            return;
        }
        ScottPlot.Fonts.AddFontFile(regular.Family, regular.Path, bold: false, italic: false);
        var bold = boldCandidates
            .Select(c => (Path: Find(c.File), c.Family))
            .FirstOrDefault(c => c.Path is not null && c.Family == regular.Family);
        if (bold.Path is not null)
            ScottPlot.Fonts.AddFontFile(bold.Family, bold.Path, bold: true, italic: false);
        ScottPlot.Fonts.Default = regular.Family;
    }

    public MainWindow()
    {
        InitializeComponent();
        // 诊断钩子: 设置 SECRANDOM_SIM_AUTORUN=1 时启动即用默认参数跑一次,
        // 便于无头截图/冒烟验证
        if (Environment.GetEnvironmentVariable("SECRANDOM_SIM_AUTORUN") == "1")
            Opened += (_, _) => OnRunClicked(null, new RoutedEventArgs());
        NuTotalDraws.ValueChanged += (_, _) => UpdateCycleInfo();
        NuStudents.ValueChanged   += (_, _) => UpdateCycleInfo();
        NuCap.ValueChanged        += (_, _) => UpdateCycleInfo();
        NuMultId.ValueChanged     += (_, _) => UpdateCycleInfo();
        NuMultK.ValueChanged      += (_, _) => UpdateCycleInfo();
        UpdateCycleInfo();
    }
    /// <summary>实时显示 总抽取数 → 周期数 的折算结果。</summary>
    private void UpdateCycleInfo()
    {
        long dpc    = ProbeConfig().DrawsPerCycle();
        long target = LongOf(NuTotalDraws, 400);
        long cycles = Math.Max(1, (target + dpc - 1) / dpc);
        CycleInfoText.Text = $"≈ {cycles:N0} 周期 × {dpc:N0} 抽 = 实际 {cycles * dpc:N0} 抽";
    }

    /// <summary>按当前面板值拼一个最小配置, 仅为拿到生效 ΣCap (倍率覆盖会改变它)。</summary>
    private SimulationConfig ProbeConfig()
    {
        var overrides = new Dictionary<int, double>();
        int multId = IntOf(NuMultId, -1);
        if (multId >= 0)
            overrides[multId] = DoubleOf(NuMultK, 1.0);
        return new SimulationConfig
        {
            StudentCount = IntOf(NuStudents, 40),
            Cap = IntOf(NuCap, 1),
            MultiplierOverrides = overrides,
        };
    }

    // ---------------------------------------------------------------- 运行

    private async void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        SimulationConfig config;
        try
        {
            config = ReadConfig();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        RunButton.IsEnabled = false;
        StatusText.Text    = "运行中…";
        RunProgress.Value  = 0;
        RunProgress.Maximum = config.Cycles;
        // Progress<T> 在创建时捕获 UI SynchronizationContext, 回调天然回到 UI 线程
        var progress = new Progress<int>(done => RunProgress.Value = done);
        try
        {
            // 计算在后台线程; await 之后回到 UI 上下文, 可以直接碰图表
            var result = await Task.Run(() => SimDriver.Run(config, progress));
            _result = result;
            StatusText.Text = $"完成 {result.TotalDraws:N0} 抽 ({config.Cycles:N0} 周期), 用时 {result.Elapsed.TotalMilliseconds:F0} ms";
            UpdateAll(result);
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            ErrorText.Text  = ex.Message;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private SimulationConfig ReadConfig()
    {
        var groups = (GenderGroupsBox.Text ?? "")
            .Split([',', '，', ';', '；', ' '],
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
            .ToArray();
        var overrides = new Dictionary<int, double>();
        int multId = IntOf(NuMultId, -1);
        if (multId >= 0)
            overrides[multId] = DoubleOf(NuMultK, 1.0);
        int drawsPerCycle = (int)new SimulationConfig
        {
            StudentCount = IntOf(NuStudents, 40),
            Cap = IntOf(NuCap, 1),
            MultiplierOverrides = overrides,
        }.DrawsPerCycle();
        // 周期制不可拆分, 向上取整保证至少抽够目标数
        int  cycles       = (int)Math.Max(1, (LongOf(NuTotalDraws, 400) + drawsPerCycle - 1) / drawsPerCycle);
        var config = new SimulationConfig
        {
            StudentCount          = IntOf(NuStudents, 40),
            Cap                   = IntOf(NuCap, 1),
            Cycles                = cycles,
            BatchSize             = IntOf(NuBatch, 1),
            Seed                  = IntOf(NuSeed, 1),
            PersonalHorizonRounds = DoubleOf(NuHorizon, 2.0),
            GenderHorizonPerPick  = DoubleOf(NuGenderHorizon, 0.8),
            RandomFloor           = DoubleOf(NuFloor, 0.10),
            GenderGroupSizes      = groups.Length == 0 ? [IntOf(NuStudents, 40)] : groups,
            MultiplierOverrides   = overrides,
        };
        config.Validate();
        return config;
    }

    private static int IntOf(NumericUpDown nud, int fallback)
        => nud.Value.HasValue ? (int)nud.Value.Value : fallback;
    private static long LongOf(NumericUpDown nud, long fallback)
        => nud.Value.HasValue ? (long)nud.Value.Value : fallback;

    private static double DoubleOf(NumericUpDown nud, double fallback)
        => nud.Value.HasValue ? (double)nud.Value.Value : fallback;

    // ---------------------------------------------------------------- 更新

    private void OnTimelineTwoCyclesClicked(object? sender, RoutedEventArgs e)
    {
        if (_result is { } result && _lastGroupOf is { } groupOf)
            UpdateTimeline(result, groupOf);
    }

    private void UpdateAll(SimulationResult result)
    {
        var groupOf = new int[result.Config.StudentCount];
        foreach (var student in result.Students)
            groupOf[student.Id] = student.Labels[0];
        _lastGroupOf = groupOf;

        UpdateMetrics(result);
        UpdateDistributions(result, groupOf);
        UpdateTimeline(result, groupOf);
        UpdatePool(result);
        UpdateLog(result);
        LogGrid.ItemsSource = result.Entries;
    }

    private void UpdateMetrics(SimulationResult result)
    {
        var metrics = MetricsCalculator.Compute(result);
        HardGrid.ItemsSource = metrics.Hard
            .Select(c => new HardRow(c.Name, c.Passed ? "✓" : "✗", c.Expected, c.Actual))
            .ToList();
        StatGrid.ItemsSource = metrics.Stats
            .Select(s => new StatRow(s.Name, s.Value))
            .ToList();
        MetricsFailNote.IsVisible = !metrics.AllHardPassed;
    }

    private void UpdateDistributions(SimulationResult result, int[] groupOf)
    {
        int n = result.Config.StudentCount;

        // 1) 每人总被抽次数, 条按性别组着色
        var countsPlot = CountsPlot.Plot;
        countsPlot.Clear();
        var counts = new double[n];
        foreach (var entry in result.Entries)
            counts[entry.PickedId]++;
        var bars = counts
            .Select((value, id) => new Bar
            {
                Position  = id,
                Value     = value,
                FillColor = Palette[groupOf[id] % Palette.Length],
            })
            .ToList();
        countsPlot.Add.Bars(bars);
        countsPlot.Axes.Bottom.Label.Text = "学生 Id";
        countsPlot.Axes.Left.Label.Text   = "总被抽次数";
        // 柱宽会把 AutoScale 的 X 范围算歪, 显式锁在 [-0.5, N-0.5]; 柱图下界恒为 0
        countsPlot.Axes.SetLimitsX(-0.5, n - 0.5);
        countsPlot.Axes.SetLimitsY(0, Math.Max(1, counts.Max() * 1.1));
        CountsPlot.Refresh();

        // 2) 同一人两次被抽间隔 (跨周期, 与指标表同口径)
        var gapPlot = GapPlot.Plot;
        gapPlot.Clear();
        var gapsData = ComputeGaps(result);
        GapOverlay.IsVisible = gapsData.Count == 0;
        if (gapsData.Count > 0)
        {
            int  binCount = Math.Clamp((int)Math.Sqrt(gapsData.Count), 5, 40);
            var  hist     = ScottPlot.Statistics.Histogram.WithBinCount(binCount, gapsData);
            var  barPlot  = gapPlot.Add.Bars(hist.Bins, hist.Counts);
            barPlot.Color = Palette[0];
            gapPlot.Axes.SetLimitsY(0, Math.Max(1, hist.Counts.Max() * 1.1));
        }
        gapPlot.Axes.Bottom.Label.Text = "间隔 (跨周期, 抽)";
        gapPlot.Axes.Left.Label.Text   = "频次";
        GapPlot.Refresh();

        // 3) BatchSize=1 时批次内不同人数恒为 1, 换「每轮见到人数」覆盖率分布:
        //    连续 N 抽为一个窗口, 统计窗口内不同学生数的直方图 (反映刺激度波动)
        var roundPlot = RoundPlot.Plot;
        roundPlot.Clear();
        if (result.Config.BatchSize <= 1)
        {
            RoundHeader.Text = $"每轮见到人数直方图 (连续 {n} 抽为一窗口, 覆盖率分布)";
            var coverage = new SortedDictionary<int, int>();
            var seen = new HashSet<int>();
            for (int i = 0; i < result.Entries.Count; i++)
            {
                seen.Add(result.Entries[i].PickedId);
                if ((i + 1) % n == 0)
                {
                    coverage[seen.Count] = coverage.GetValueOrDefault(seen.Count) + 1;
                    seen.Clear();
                }
            }
            if (coverage.Count > 0)
            {
                var positions = coverage.Keys.Select(k => (double)k).ToArray();
                var values    = coverage.Values.Select(v => (double)v).ToArray();
                var barPlot   = roundPlot.Add.Bars(positions, values);
                barPlot.Color = Palette[2];
                roundPlot.Axes.SetLimitsY(0, Math.Max(1, values.Max() * 1.1));
            }
            roundPlot.Axes.Bottom.Label.Text = "窗口内见到不同人数";
            roundPlot.Axes.Left.Label.Text   = "窗口数";
        }
        else
        {
            RoundHeader.Text = "批次内不同人数直方图 (应恒等于 BatchSize)";
            var frequency = new SortedDictionary<int, int>();
            foreach (int distinct in BatchDistinctCounts(result.Entries))
                frequency[distinct] = frequency.GetValueOrDefault(distinct) + 1;
            var positions = frequency.Keys.Select(k => (double)k).ToArray();
            var values    = frequency.Values.Select(v => (double)v).ToArray();
            if (positions.Length > 0)
            {
                var barPlot  = roundPlot.Add.Bars(positions, values);
                barPlot.Color = Palette[2];
                roundPlot.Axes.SetLimitsY(0, Math.Max(1, values.Max() * 1.1));
            }
            roundPlot.Axes.Bottom.Label.Text = "批次内不同人数";
            roundPlot.Axes.Left.Label.Text   = "批次数";
        }
        RoundPlot.Refresh();
    }

    private void UpdateTimeline(SimulationResult result, int[] groupOf)
    {
        var plot    = TimelinePlot.Plot;
        plot.Clear();
        var config  = result.Config;
        long perCycle   = config.DrawsPerCycle();
        int shownCycles = TimelineTwoCycles.IsChecked == true
            ? Math.Min(2, config.Cycles)
            : Math.Min(config.Cycles, (int)Math.Max(1, TimelinePointBudget / perCycle));
        bool truncated  = shownCycles < config.Cycles;
        TimelineNote.Text = truncated
            ? $"仅显示前 {shownCycles}/{config.Cycles} 周期 (共 {perCycle * config.Cycles:N0} 点)"
            : "";
        long shown = (long)shownCycles * perCycle;

        int groups = config.GenderGroupSizes.Length;
        var xs = Enumerable.Range(0, groups).Select(_ => new List<double>()).ToArray();
        var ys = Enumerable.Range(0, groups).Select(_ => new List<double>()).ToArray();
        for (int i = 0; i < shown && i < result.Entries.Count; i++)
        {
            var entry = result.Entries[i];
            int group = groupOf[entry.PickedId];
            xs[group].Add(entry.GlobalIndex);
            ys[group].Add(entry.PickedId);
        }
        for (int group = 0; group < groups; group++)
        {
            if (xs[group].Count == 0) continue;
            var scatter = plot.Add.Scatter(xs[group].ToArray(), ys[group].ToArray());
            scatter.LineWidth   = 0;   // 只画点不连线 (LinePattern 无 None, 用线宽 0)
            scatter.MarkerSize  = 2;
            scatter.Color       = Palette[group % Palette.Length];
            scatter.LegendText  = $"组{group}";
        }
        if (groups > 1)
            plot.ShowLegend();

        if (shownCycles <= 200)
            for (int cycle = 1; cycle < shownCycles; cycle++)
                plot.Add.VerticalLine(cycle * perCycle).Color = BoundaryColor;

        plot.Axes.Bottom.Label.Text = "全局抽取序号";
        plot.Axes.Left.Label.Text   = "学生 Id";
        TimelinePlot.Refresh();
    }

    private void UpdatePool(SimulationResult result)
    {
        var plot = PoolPlot.Plot;
        plot.Clear();
        var poolSizes = result.Entries.Select(e => (double)e.PoolSize).ToArray();
        if (poolSizes.Length > 0)
        {
            var signal = plot.Add.Signal(poolSizes);
            signal.Color = Palette[0];
        }
        long perCycle = result.DrawsPerCycle;
        int cycles    = result.Config.Cycles;
        if (cycles <= 400)
            for (int cycle = 1; cycle < cycles; cycle++)
                plot.Add.VerticalLine(cycle * perCycle).Color = BoundaryColor;

        plot.Axes.Bottom.Label.Text = "全局抽取序号";
        plot.Axes.Left.Label.Text   = "池内人数";
        PoolPlot.Refresh();
    }

    private void UpdateLog(SimulationResult result)
    {
        LogCountText.Text = $"{result.Entries.Count:N0} 行";
    }

    // ---------------------------------------------------------------- 数据加工

    /// <summary>同一人两次相邻被抽的间隔 (跨周期, 按全局序号), 与指标表口径一致。</summary>
    private static List<double> ComputeGaps(SimulationResult result)
    {
        int n = result.Config.StudentCount;
        var gaps     = new List<double>();
        var lastSeen = new int[n];
        Array.Fill(lastSeen, -1);
        foreach (var entry in result.Entries)
        {
            if (lastSeen[entry.PickedId] >= 0)
                gaps.Add(entry.GlobalIndex - lastSeen[entry.PickedId]);
            lastSeen[entry.PickedId] = entry.GlobalIndex;
        }
        return gaps;
    }

    /// <summary>每个批次 (BatchSlot 从 0 重数的一段) 内不同 PickedId 的数量。</summary>
    private static List<int> BatchDistinctCounts(IReadOnlyList<DrawLogEntry> entries)
    {
        var counts = new List<int>();
        var seen   = new HashSet<int>();
        int prevSlot = -1;
        foreach (var entry in entries)
        {
            if (entry.BatchSlot <= prevSlot)
            {
                counts.Add(seen.Count);
                seen.Clear();
            }
            seen.Add(entry.PickedId);
            prevSlot = entry.BatchSlot;
        }
        if (seen.Count > 0)
            counts.Add(seen.Count);
        return counts;
    }

    // ---------------------------------------------------------------- 导出

    private async void OnExportCsvClicked(object? sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            ErrorText.Text = "还没有可导出的仿真结果";
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "导出仿真日志",
            SuggestedFileName = $"drawlog_seed{_result.Config.Seed}_n{_result.Config.StudentCount}_cap{_result.Config.Cap}_c{_result.Config.Cycles}.csv",
            DefaultExtension  = "csv",
            FileTypeChoices   = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            DrawLog.WriteCsv(writer, _result.Entries);
            StatusText.Text = $"已导出 {_result.Entries.Count:N0} 行: {file.Path.LocalPath}";
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"导出失败: {ex.Message}";
        }
    }

    private sealed record HardRow(string Name, string Mark, string Expected, string Actual);
    private sealed record StatRow(string Name, string Value);
}
