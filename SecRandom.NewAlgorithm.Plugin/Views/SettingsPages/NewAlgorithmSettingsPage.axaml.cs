using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;

namespace SecRandom.NewAlgorithm.Plugin.Views.SettingsPages;

[PageInfo("plugin.secrandom.newalgorithm.settings", FluentIcons.ScaleFillFilled)]
public partial class NewAlgorithmSettingsPage : UserControl
{
    public NewAlgorithmSettingsPage(NewAlgorithmOptionsStore store)
    {
        Store = store;
        InitializeComponent();
        DataContext = this;
    }

    private NewAlgorithmOptionsStore Store { get; }

    // NumericUpDown works in decimal; these proxies keep the persisted options in double.
    public decimal? PersonalHorizonRoundsValue
    {
        get => (decimal)Store.Current.PersonalHorizonRounds;
        set
        {
            Store.Current.PersonalHorizonRounds = value is { } v ? (double)v : 2.0;
            Store.Save();
        }
    }

    public decimal? RandomFloorValue
    {
        get => (decimal)Store.Current.RandomFloor;
        set
        {
            Store.Current.RandomFloor = value is { } v ? (double)v : 0.10;
            Store.Save();
        }
    }

    public decimal? HorizonPerPickValue
    {
        get => (decimal)Store.Current.DimensionHorizonPerPick;
        set
        {
            Store.Current.DimensionHorizonPerPick = value is { } v ? (double)v : 0.8;
            Store.Save();
        }
    }
}
