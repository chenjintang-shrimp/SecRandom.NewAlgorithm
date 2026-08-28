using System.Text.Json;

namespace SecRandom.NewAlgorithm.Plugin;

/// <summary>
///     Loads and persists <see cref="NewAlgorithmOptions" /> inside the plugin config folder.
///     Saves are atomic replacements (temporary file + move), matching the host persistence
///     convention; a corrupted file falls back to defaults instead of killing the draw pipeline.
/// </summary>
public sealed class NewAlgorithmOptionsStore
{
    /// <summary>Plugin settings page and the draw algorithm both read/write through this bounds.</summary>
    public const double MinHorizonRounds = 0.1;
    public const double MaxHorizonRounds = 100.0;
    public const double MinRandomFloor = 0.0;
    public const double MaxRandomFloor = 0.99;
    public const double MinHorizonPerPick = 0.05;
    public const double MaxHorizonPerPick = 100.0;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _filePath;
    private NewAlgorithmOptions? _options;

    public NewAlgorithmOptionsStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "options.json");
    }

    public NewAlgorithmOptions Current
    {
        get
        {
            lock (_gate)
                return _options ??= Load();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var snapshot = _options ?? new NewAlgorithmOptions();
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, s_jsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
    }

    private NewAlgorithmOptions Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var options = JsonSerializer.Deserialize<NewAlgorithmOptions>(File.ReadAllText(_filePath));
                if (options is not null)
                {
                    Sanitize(options);
                    return options;
                }
            }
        }
        catch (Exception)
        {
            // Corrupted or legacy file: fall back to defaults. The broken file is left in place
            // and will be overwritten by the next successful save.
        }

        return new NewAlgorithmOptions();
    }

    private static void Sanitize(NewAlgorithmOptions options)
    {
        options.PersonalHorizonRounds = ClampFinite(options.PersonalHorizonRounds, MinHorizonRounds, MaxHorizonRounds, 2.0);
        options.RandomFloor = ClampFinite(options.RandomFloor, MinRandomFloor, MaxRandomFloor, 0.10);
        options.DimensionHorizonPerPick = ClampFinite(options.DimensionHorizonPerPick, MinHorizonPerPick, MaxHorizonPerPick, 0.8);
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);
    }
}
