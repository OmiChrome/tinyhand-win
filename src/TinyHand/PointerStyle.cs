namespace TinyHand;

/// <summary>A hand (built in or custom) or the laser dot.</summary>
internal sealed record PointerStyle(string Id)
{
    public static readonly PointerStyle Lavender = new("lavender");
    public static readonly PointerStyle Glove = new("glove");
    public static readonly PointerStyle Coral = new("coral");
    public static readonly PointerStyle Plush = new("plush");
    public static readonly PointerStyle Robot = new("robot");
    public static readonly PointerStyle Laser = new("laser");

    private static readonly (string Id, string Title, Rgb Ink)[] BuiltIn =
    {
        ("lavender", "Lavender toy", Rgb.Purple),
        ("glove", "Cursor glove", Rgb.Purple),
        ("coral", "Coral vinyl", Rgb.Red),
        ("plush", "Plush", new(0x2F86F0)),
        ("robot", "Robot", new(0x16A87A)),
        ("pink", "Bubblegum", new(0xEE4F98)),
        ("blue", "Sky", new(0x2F86F0)),
        ("mint", "Mint", new(0x16A87A)),
        ("lemon", "Lemon", new(0xF2B705)),
        ("black", "Midnight", new(0x1D1D21)),
        ("chrome", "Chrome", new(0x6B7380)),
        ("holo", "Holo", Rgb.Purple),
        ("fuzzy", "Fuzzy", Rgb.Purple),
        ("wood", "Wood", new(0x9A5E2C)),
        ("marble", "Marble", new(0x6B7380)),
        ("terrazzo", "Terrazzo", new(0xF0561D)),
        ("glass", "Glass", new(0x1597E5)),
        ("jelly", "Jelly", new(0xA33FE6)),
        ("pastel", "Pastel", new(0xEE4F98)),
        ("pixel", "Pixel", new(0x1D1D21)),
        ("lowpoly", "Low poly", Rgb.Purple),
        ("knit", "Knit", Rgb.Purple),
        ("skeleton", "Skeleton", new(0x9A5E2C)),
        ("astro", "Astronaut", new(0x2F86F0)),
        ("space", "Space", Rgb.Red),
        ("monster", "Monster", new(0x3E9E2E)),
        ("water", "Water", new(0x1597E5)),
        ("lava", "Lava", new(0xF0561D)),
        ("grass", "Grass", new(0x3E9E2E)),
        ("cactus", "Cactus", new(0x3E9E2E)),
        ("choco", "Chocolate", new(0x9A5E2C)),
        ("cloud", "Cloud", new(0x2F86F0)),
        ("mech", "Mech", new(0xF0561D)),
        ("neon", "Neon", new(0xE0309A)),
        ("leaf", "Leaf", new(0x3E9E2E)),
        ("cookie", "Gingerbread", new(0x9A5E2C)),
    };

    public static IReadOnlyList<PointerStyle> DefaultPinned { get; } =
        new[] { Lavender, Glove, Coral, Plush, Robot, Laser };

    public static IEnumerable<PointerStyle> BuiltInHands => BuiltIn.Select(b => new PointerStyle(b.Id));

    public static IEnumerable<PointerStyle> CustomHandStyles => CustomHands.All.Select(h => new PointerStyle(h.Id));

    public static IEnumerable<PointerStyle> All => BuiltInHands.Concat(CustomHandStyles).Append(Laser);

    public static PointerStyle? Find(string id)
    {
        if (id == "laser" || id == "dot")
        {
            return Laser;
        }
        if (id == "sunny")
        {
            return Coral;
        }
        if (BuiltIn.Any(b => b.Id == id) || CustomHands.Find(id) != null)
        {
            return new PointerStyle(id);
        }
        return null;
    }

    public bool IsLaser => Id == Laser.Id;

    public bool IsCustom => CustomHands.Find(Id) != null;

    public string Title
    {
        get
        {
            if (IsLaser)
            {
                return "Laser dot";
            }
            if (CustomHands.Find(Id) is { } custom)
            {
                return custom.Name;
            }
            return BuiltIn.FirstOrDefault(b => b.Id == Id).Title ?? Id;
        }
    }

    /// <summary>File name (without .png) of the hand image, or null for the laser.</summary>
    public string? AssetName => IsLaser ? null : IsCustom ? Id : $"hand_{Id}";

    public Rgb InkColor
    {
        get
        {
            if (IsLaser)
            {
                return Rgb.Red;
            }
            if (CustomHands.Find(Id) is { } custom)
            {
                return new Rgb(custom.Ink);
            }
            var match = BuiltIn.FirstOrDefault(b => b.Id == Id);
            return match.Id == null ? Rgb.Purple : match.Ink;
        }
    }

    public static PointerStyle Saved
    {
        get => Find(Preferences.PointerStyleId) ?? Lavender;
        set => Preferences.PointerStyleId = value.Id;
    }
}
