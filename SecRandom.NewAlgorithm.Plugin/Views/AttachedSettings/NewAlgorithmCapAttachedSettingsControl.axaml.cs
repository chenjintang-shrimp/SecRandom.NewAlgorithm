using System;
using System.ComponentModel;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;

namespace SecRandom.NewAlgorithm.Plugin.Views.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student)]
[AttachedSettingsControlInfo(NewAlgorithmStudentAttachedSettings.AttachedSettingsId, FluentIcons.ScaleFillFilled)]
public partial class NewAlgorithmCapAttachedSettingsControl :
    AttachedSettingsControlBase<NewAlgorithmStudentAttachedSettings>,
    INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public NewAlgorithmCapAttachedSettingsControl()
    {
        InitializeComponent();
    }

    public double? ShareCapValue
    {
        get => Settings.ShareCap;
        set
        {
            var cap = Math.Clamp(value ?? 1, 1, 1000);
            if (Math.Abs(Settings.ShareCap - cap) < double.Epsilon)
                return;

            Settings.ShareCap = cap;
            OnPropertyChanged(nameof(ShareCapValue));
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged(string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
