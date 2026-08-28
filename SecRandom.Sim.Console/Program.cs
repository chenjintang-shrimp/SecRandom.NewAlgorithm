using SecRandom.Sim;

// 硬保证验收入口: 跑一组代表性场景, 任何一条硬保证失败即非零退出。
var scenarios = new (string Name, SimulationConfig Config)[]
{
    ("默认 40人 Cap1 10周期", new SimulationConfig()),
    ("Cap3 20周期 (间隔统计)", new SimulationConfig { Cap = 3, Cycles = 20 }),
    ("小池 5人 Cap2 50周期", new SimulationConfig { StudentCount = 5, Cap = 2, Cycles = 50, GenderGroupSizes = [3, 2] }),
    ("不均衡分组 30/10 Cap2", new SimulationConfig { Cap = 2, GenderGroupSizes = [30, 10] }),
    ("大视野 (近纯随机) H=8", new SimulationConfig { PersonalHorizonRounds = 8.0 }),
    ("硬视野 (近点名册) H=0.5", new SimulationConfig { PersonalHorizonRounds = 0.5, Cycles = 30 }),
    ("RandomFloor=0 (退化压力大)", new SimulationConfig { RandomFloor = 0.0, Cycles = 30 }),
    ("三组 10/15/15 Cap2", new SimulationConfig { Cap = 2, GenderGroupSizes = [10, 15, 15] }),
    ("批量 BatchSize=4 Cap1", new SimulationConfig { BatchSize = 4 }),
    ("批量 BatchSize=4 Cap3", new SimulationConfig { Cap = 3, BatchSize = 4 })
};

var failures = 0;
foreach (var (name, config) in scenarios)
{
    Console.WriteLine($"=== {name} ===");
    SimulationResult result;
    try
    {
        result = SimDriver.Run(config);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  仿真执行异常: {ex.Message}");
        failures++;
        continue;
    }

    var metrics = MetricsCalculator.Compute(result);
    foreach (var check in metrics.Hard)
        Console.WriteLine($"  [{(check.Passed ? '✓' : '✗')}] {check.Name}: {check.Actual} (期望 {check.Expected})");
    foreach (var stat in metrics.Stats)
        Console.WriteLine($"      {stat.Name}: {stat.Value}");
    if (!metrics.AllHardPassed)
        failures++;
    Console.WriteLine();
}

Console.WriteLine(failures == 0 ? "全部硬保证通过" : $"{failures} 个场景存在硬保证失败");
return failures == 0 ? 0 : 1;
