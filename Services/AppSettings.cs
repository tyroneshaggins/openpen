using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using OpenPen.Models;

namespace OpenPen.Services;

public sealed class AppSettings
{
    public string InkColor { get; set; } = "#FFFFD400";
    public string LastColor { get; set; } = "#FFFFFFFF";
    public double PenSize { get; set; } = 4;
    public double HighlighterSize { get; set; } = 20;
    public double EraserSize { get; set; } = 28;
    public double ShapeSize { get; set; } = 4;
    public double TextSize { get; set; } = 28;
    public double InkOpacity { get; set; } = 1.0;
    public FadingMode Fading { get; set; } = FadingMode.Off;
    public double SpotlightRadius { get; set; } = 170;
    public bool HotkeysEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public double? ToolbarX { get; set; }
    public double? ToolbarY { get; set; }
    public ToolKind LastDrawingTool { get; set; } = ToolKind.Pen;
    public bool FirstRunDone { get; set; }

    [JsonIgnore]
    public Color InkMediaColor => ColorUtil.TryParse(InkColor, out var color) ? color : ColorUtil.DefaultInk;

    [JsonIgnore]
    public Color LastMediaColor => ColorUtil.TryParse(LastColor, out var color) ? color : Colors.White;

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenPen");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt or unreadable settings file should never prevent OpenPen from starting.
            try
            {
                if (File.Exists(SettingsPath))
                {
                    File.Copy(SettingsPath, SettingsPath + ".bak", overwrite: true);
                }
            }
            catch
            {
                // ignored
            }
        }

        return new AppSettings();
    }

    public void Normalize()
    {
        PenSize = Math.Clamp(PenSize, 1, 64);
        HighlighterSize = Math.Clamp(HighlighterSize, 4, 96);
        EraserSize = Math.Clamp(EraserSize, 6, 120);
        ShapeSize = Math.Clamp(ShapeSize, 1, 32);
        TextSize = Math.Clamp(TextSize, 10, 120);
        InkOpacity = Math.Clamp(InkOpacity, 0.1, 1.0);
        SpotlightRadius = Math.Clamp(SpotlightRadius, 70, 420);
        if (LastDrawingTool == ToolKind.PassThrough || LastDrawingTool == ToolKind.Eraser)
        {
            LastDrawingTool = ToolKind.Pen;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch
        {
            // Preferences are a convenience; never crash over them.
        }
    }
}
