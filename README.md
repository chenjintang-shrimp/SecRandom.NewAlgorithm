<div align=center>

# SecRandom.NewAlgorithm（别名VisionFair）

为 SecRandom V3 带来的全新伪随机算法。

![License: MIT](https://img.shields.io/badge/License-MIT-blue)
![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue)

</div>

> [!WARNING]
> 该算法与“平均值差值保护”之间冲突。如果需要体验完整算法请将其关闭：我们的流程中已经包含了这一点。

## 许可证问题

> [!NOTE]
> 本项目采用 GPL-3/MIT双许可。详细请见[LICENSE]("LICENSE")文件。

## 渊源

当前，SecRandom 3的算法来自于老旧的SecRandom 2。这个算法非常复杂，而且在数学上并不自洽。甚至可以说，原来的算法无法起到**任何**的“公平”（实际上是模拟人类对随机的认知）的作用，而真正的所谓“公平”功能实则集中在“平均值查值保护”这一功能上。SecRandom 3 在 SecRandom 2上砍掉了不少可以自定义的参数——实际上这些参数（本来都可以自定义）大部分都是调了却不知道是什么鬼的。

所以，在 Claude Opus 5 和 Kimi K3 的帮助下我研究并开发了这个新算法。当前这个项目以插件的形式存在，但是在未来的某一天，该算法或许将会正式并入 SecRandom 主线实现中，用于取代老的算法。

## 项目概要

目前仓库由五个 C# 项目组成：[SecRandom.NewAlgorithm](SecRandom.NewAlgorithm) 中包含了主要的算法实现，[SecRandom.NewAlgorithm.Plugin](SecRandom.NewAlgorithm.Plugin) 中包含了针对 SecRandom 3 的插件。[SecRandom.Sim](SecRandom.Sim) 则是用于测试算法的核心，[SecRandom.Sim.Avalonia](SecRandom.Sim.Avalonia) 是用于以可视化方式展示测试结果的工具，[SecRandom.Sim.Console](SecRandom.Sim.Console) 则是其命令行版本。

## 技术细节

详见[TECH.md](tech.md)。

## 鸣谢

本项目引用了[SecRandom.PluginSDK]("https://github.com/SECTL/SecRandom")，使用 Avalonia 作为图形化测试工具所使用的图形库。

Copyright (C) 2026 chenjintang-shrimp
