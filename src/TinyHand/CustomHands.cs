using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyHand;

internal sealed record CustomHand(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ink")] uint Ink);

/// <summary>Hands the user made, stored in %APPDATA%\TinyHand\Hands as PNGs plus hands.json.</summary>
internal static class CustomHands
{
    public static event Action? Changed;

    public static string Directory
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("TH_HANDS_DIR");
            return string.IsNullOrEmpty(overrideDir) ? Path.Combine(Paths.RoamingData, "Hands") : overrideDir;
        }
    }

    private static string ListPath => Path.Combine(Directory, "hands.json");

    private static List<CustomHand>? loaded;
    private static readonly object Gate = new();

    public static IReadOnlyList<CustomHand> All
    {
        get
        {
            lock (Gate)
            {
                if (loaded != null)
                {
                    return loaded;
                }
                try
                {
                    loaded = File.Exists(ListPath)
                        ? JsonSerializer.Deserialize<List<CustomHand>>(File.ReadAllText(ListPath)) ?? new()
                        : new();
                }
                catch (Exception e)
                {
                    Log.Write($"hands.json unreadable: {e.Message}");
                    loaded = new();
                }
                return loaded;
            }
        }
    }

    public static CustomHand? Find(string id) =>
        id.StartsWith("custom-", StringComparison.Ordinal) ? All.FirstOrDefault(h => h.Id == id) : null;

    public static string ImagePath(string id) => Path.Combine(Directory, $"{id}.png");

    public static PointerStyle Add(byte[] png, uint ink, string name)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllBytes(ImagePath(id), png);
        var trimmed = name.Trim();
        var list = All.ToList();
        var finalName = trimmed.Length == 0 ? $"My hand {list.Count + 1}" : trimmed[..Math.Min(16, trimmed.Length)];
        list.Add(new CustomHand(id, finalName, ink));
        Save(list);
        return new PointerStyle(id);
    }

    public static void Remove(PointerStyle style)
    {
        if (!style.IsCustom)
        {
            return;
        }
        try
        {
            File.Delete(ImagePath(style.Id));
        }
        catch (Exception e)
        {
            Log.Write($"could not delete {style.Id}: {e.Message}");
        }
        HandAsset.Forget(style.AssetName!);
        Save(All.Where(h => h.Id != style.Id).ToList());
    }

    public static string SuggestedName(string path)
    {
        var words = string.Join(' ', Path.GetFileNameWithoutExtension(path)
            .Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (words.Length == 0 || words.Length > 16 || words.StartsWith("chatgpt image", StringComparison.OrdinalIgnoreCase))
        {
            return $"My hand {All.Count + 1}";
        }
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static void Save(List<CustomHand> list)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(ListPath, JsonSerializer.Serialize(list));
        lock (Gate)
        {
            loaded = list;
        }
        Changed?.Invoke();
    }
}
