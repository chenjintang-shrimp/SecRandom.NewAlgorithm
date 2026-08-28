using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Interfaces;

namespace SecRandom.NewAlgorithm.Plugin;

/// <summary>
///     Per-student attached setting for the share-debt algorithm: the student's share cap
///     (欠账份额上限). The host's attached-settings presenter persists this through the profile
///     like any other attached settings entry; it must stay observable so edits trigger
///     write-back (see AttachedSettingsControlPresenter).
/// </summary>
public partial class NewAlgorithmStudentAttachedSettings : ObservableObject, IAttachedSettings
{
    public const string AttachedSettingsId = "7F3E9A2C-1D4B-4C8E-B6A5-9E0F1D2C3B4A";

    [ObservableProperty]
    private bool _isAttachSettingsEnabled;

    /// <summary>欠账份额 Cap：share = Cap ÷ ΣCap，默认 1（与所有人等份额）</summary>
    [ObservableProperty]
    private double _shareCap = 1;
}
