using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Interfaces;

namespace SecRandom.NewAlgorithm.Plugin;

/// <summary>
///     Per-student attached setting for the share-debt algorithm: the student's base draw cap
///     (个人抽取次数上限, 随内幕倍率放大). The host's attached-settings presenter persists this through the profile
///     like any other attached settings entry; it must stay observable so edits trigger
///     write-back (see AttachedSettingsControlPresenter).
/// </summary>
public partial class NewAlgorithmStudentAttachedSettings : ObservableObject, IAttachedSettings
{
    public const string AttachedSettingsId = "7F3E9A2C-1D4B-4C8E-B6A5-9E0F1D2C3B4A";

    [ObservableProperty] private bool _isAttachSettingsEnabled;

    /// <summary>
    ///     基础抽取次数上限：该生累计被抽次数达到 ⌈基础 × 内幕倍率⌉ 后移出候选池。
    ///     纯安全阀，不进权重；默认 1，未启用时无个人上限
    /// </summary>
    [ObservableProperty] private double _baseCap = 1;
}
