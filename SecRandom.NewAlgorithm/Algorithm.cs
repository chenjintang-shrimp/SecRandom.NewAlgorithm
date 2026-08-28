namespace SecRandom.NewAlgorithm;

public static class FairDrawWeights
{
    public static WeightResult Compute(
        IReadOnlyList<StudentMetaData> pool,
        IReadOnlyList<DrawHistory>     histories,
        WeightSettings                 settings,
        int                            batchSize = 1
    )
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        if (pool.Count == 0)
            throw new ArgumentException("候选池为空; 周期已抽干, 应由调用方重置计数", nameof(pool));
        if (batchSize > pool.Count)
            throw new ArgumentOutOfRangeException(nameof(batchSize),
                $"批量 {batchSize} 超过池内人数 {pool.Count}; 上限可能已把池子抽小");

        int    poolSize        = pool.Count;
        var    drawCountById   = histories.ToDictionary(record => record.Id, record => (double)record.DrawCount);
        var    drawCount       = new double[poolSize];
        double multiplierSum   = 0.0;
        var    share           = new double[poolSize];

        for (int index = 0; index < poolSize; index++)
        {
            var student = pool[index];
            if (student.Multiplier <= 0.0 || double.IsNaN(student.Multiplier) || double.IsInfinity(student.Multiplier))
                throw new ArgumentException($"学生 {student.Id} 的倍率 {student.Multiplier} 必须为正的有限值", nameof(pool));
            double count = drawCountById.GetValueOrDefault(student.Id, 0.0);
            if (count < 0.0)
                throw new ArgumentException($"学生 {student.Id} 的次数为负", nameof(histories));
            drawCount[index] =  count;
            multiplierSum    += student.Multiplier;
        }

        double totalDraws = 0.0;
        for (int index = 0; index < poolSize; index++)
        {
            share[index] =  pool[index].Multiplier / multiplierSum;
            totalDraws   += drawCount[index];
        }

        double personalHorizon = settings.PersonalHorizonRounds * poolSize;
        var    personalDebt    = new double[poolSize];
        var    weight          = new double[poolSize];
        for (int index = 0; index < poolSize; index++)
        {
            personalDebt[index] = Math.Max(
                share[index] * (totalDraws + personalHorizon) - drawCount[index], 0.0
            );
            weight[index] = personalDebt[index];
        }

        int dimensionCount = settings.Dimensions.Length;
        var dimensionDebt  = new double[poolSize][];
        for (int index = 0; index < poolSize; index++)
            dimensionDebt[index] = new double[dimensionCount];
        for (int slot = 0; slot < dimensionCount; slot++)
        {
            var dimension = settings.Dimensions[slot];
            if (dimension.Dimension < 0)
                throw new ArgumentException("维度下标不能为负", nameof(settings));
            int labelCount = 0;
            for (int index = 0; index < poolSize; index++)
            {
                var labels = pool[index].Labels;
                if (dimension.Dimension >= labels.Length)
                    throw new ArgumentException(
                        $"学生 {pool[index].Id} 缺少维度 {dimension.Dimension} 的标签", nameof(pool));
                int label = labels[dimension.Dimension];
                if (label < 0)
                    throw new ArgumentException($"学生 {pool[index].Id} 的标签为负", nameof(pool));
                labelCount = Math.Max(labelCount, label + 1);
            }
            var labelShare = new double[labelCount];
            var labelDrawn = new double[labelCount];
            for (int index = 0; index < poolSize; index++)
            {
                int label = pool[index].Labels[dimension.Dimension];
                labelShare[label] += share[index];
                labelDrawn[label] += drawCount[index];
            }
            double horizon = dimension.HorizonPerPick * batchSize;
            var    debt    = new double[labelCount];
            for (int label = 0; label < labelCount; label++)
                debt[label] = Math.Max(labelShare[label] * (totalDraws + horizon) - labelDrawn[label], 0.0);
            for (int index = 0; index < poolSize; index++)
            {
                double value = debt[pool[index].Labels[dimension.Dimension]];
                dimensionDebt[index][slot] =  value;
                weight[index]              *= value;
            }
        }
        double weightSum = weight.Sum();
        bool   degraded  = weightSum <= 0.0;
        if (degraded)
        {
            Array.Fill(weight, 1.0);
            weightSum = poolSize;
        }
        var candidates = new CandidateWeight[poolSize];
        for (int index = 0; index < poolSize; index++)
        {
            candidates[index] = new CandidateWeight(
                Id: pool[index].Id,
                Weight: weight[index],
                PersonalDebt: personalDebt[index],
                DimensionDebts: dimensionDebt[index]);
        }
        return new WeightResult(candidates, weightSum, degraded, poolSize == 1);
    }

    public static double[] ToProbabilities(WeightResult result, WeightSettings settings)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        double floor = settings.RandomFloor;
        if (floor is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(settings), "RandomFloor 须在 [0, 1] 内");
        int    poolSize      = result.Candidates.Count;
        double floorShare    = floor / poolSize;
        var    probabilities = new double[poolSize];
        for (int index = 0; index < poolSize; index++)
            probabilities[index] =
                (1.0 - floor) * result.Candidates[index].Weight / result.WeightSum + floorShare;
        return probabilities;
    }
}
