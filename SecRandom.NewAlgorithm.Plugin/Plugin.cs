using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Extensions.Registry;
using SecRandom.NewAlgorithm.Plugin.Views.AttachedSettings;
using SecRandom.NewAlgorithm.Plugin.Views.SettingsPages;
using SecRandom.PluginSdk;

namespace SecRandom.NewAlgorithm.Plugin;

public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // PluginConfigFolder is assigned by the host before Initialize and pre-created.
        var optionsStore = new NewAlgorithmOptionsStore(PluginConfigFolder);
        services.AddSingleton(optionsStore);

        services.AddRollCallAlgorithm<DebtWeightRollCallAlgorithm>(
            "newalgorithm.debtshare",
            "份额欠账均衡");
        services.AddSettingsPage<NewAlgorithmSettingsPage>("份额欠账算法设置");
        services.AddAttachedSettingsControl<NewAlgorithmCapAttachedSettingsControl>("个人抽取上限");
    }
}
