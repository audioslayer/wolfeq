using System.IO;
using System.Windows;
using System.Windows.Media;

namespace WolfEQ.Services;

public static class BackgroundThemeService
{
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "background-theme.txt");
    public static string CurrentMode => Application.Current?.TryFindResource("WolfBackgroundMode") as string ?? "Dark";

    public static string Load()
    {
        try { return File.ReadAllText(PreferencePath).Trim() == "Light" ? "Light" : "Dark"; }
        catch (IOException) { return "Dark"; }
        catch (UnauthorizedAccessException) { return "Dark"; }
    }

    public static void Save(string mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, mode);
    }

    public static void Apply(string mode)
    {
        var light = mode == "Light";
        var keys = new[] { "Bg", "Shell", "Panel", "Panel2", "Line", "LineSoft", "Text", "Muted", "Dim", "Warning", "Danger" };
        var colors = light
            ? new[] { "#EDF2F5", "#F5F8FA", "#FFFFFF", "#E6EDF2", "#C6D2DA", "#9DAFBC", "#172A35", "#405967", "#526A78", "#936000", "#C32C3E" }
            : new[] { "#0C1419", "#101B21", "#111C22", "#17242C", "#253740", "#334752", "#F0F5F7", "#A7BDC9", "#90A8B6", "#FFB800", "#FF5555" };
        var resources = Application.Current.Resources;
        resources["WolfBackgroundMode"] = light ? "Light" : "Dark";
        for (var i = 0; i < keys.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString(colors[i]);
            resources["Wolf" + keys[i]] = color;
            resources["Wolf" + keys[i] + "Brush"] = new SolidColorBrush(color);
        }
    }
}
