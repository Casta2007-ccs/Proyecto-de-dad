using System.Collections.Frozen;
using System.Text.Json;
using OpenCvSharp;

public class GameRepository
{
    private const string UserProfilesJsonPath = "Assets/user_profiles.json";
    private const string LeaderboardJsonPath = "leaderboard.json";

    private static readonly FrozenSet<string> ValidExtensions =
        new[] { ".jpg", ".jpeg", ".png", ".webp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Key, string Description)[] KnownDescriptions =
    [
        // Expedientes Interpol
        ("petrov", "Viktor Petrov - Fraude transnacional criptomonedas y evasión fiscal masiva"),
        ("costa", "Marina Costa - Contrabando internacional de artefactos históricos de alto valor"),
        ("chen_wei", "Chen Wei - Operaciones clandestinas internacionales de ciberespionaje industrial"),
        ("kravchenko", "Igor Kravchenko - Tráfico ilícito internacional de armamento militar pesado"),
        ("santoro", "Matteo Santoro - Extorsión bancaria masiva y sabotaje a infraestructuras críticas"),
        ("jose_acosta", "Jose Acosta - Porte ilícito de flow desmedido y perturbación estética del orden público"),
        ("klaus_schmidt", "Klaus Schmidt - Ciberdelincuente internacional especializado en ransomware bancario"),
        ("elena_rostova", "Elena Rostova - Cabecilla de red de falsificación de obras maestras y blanqueo"),
        ("marco_rossi", "Marco Rossi - Prófugo por evasión fiscal de 80M€ y apuestas clandestinas"),
        ("tariq_almansoor", "Tariq Al-Mansoor - Contrabando marítimo transfronterizo de hidrocarburos"),
        ("lucas_silva", "Lucas Silva - Creador de mega-estafa piramidal con tokens cripto ficticios"),
        ("sofia_valdez", "Sofía Valdez - Lavado de activos mediante fideicomisos opacos offshore"),
        ("dimitri_volkov", "Dimitri Volkov - Notificación Roja: Ciberataques a redes de distribución energética"),
        ("carmen_delgado", "Carmen Delgado - Tráfico internacional de diamantes en bruto y contrabando"),
        ("kenji_sato", "Kenji Sato - Falsificación a gran escala de bonos soberanos y pasaportes"),
        ("andre_dupont", "André Dupont - Desfalco transnacional a fondos de pensiones y fuga en velero"),

        // Perfiles Ejecutivos LinkedIn
        ("mikkelsen", "Lars Mikkelsen - Chief Visionary Officer & Strategic Business Growth Lead"),
        ("horvath", "Pavol Horváth - VP of People & Culture | Agile Mindset Coach | Keynote Speaker"),
        ("kovalenko", "Yuri Kovalenko - Head of Global Synergy & Disruptive Tech Solutions"),
        ("tshabalala", "Sipho Tshabalala - Senior Talent Acquisition Partner | Ex-Googler | Forbes 30u30"),
        ("jensen", "Mikkel Jensen - Enterprise Cloud Transformation Specialist & Agile Scrum Master"),
        ("sarah_jenkins", "Sarah Jenkins - Global Head of Agile Transformation & Disruptive Mindfulness"),
        ("david_kim", "David Kim - VP of Web3 Ecosystems & Tokenized Architecture | Ex-Meta"),
        ("clara_fontana", "Clara Fontana - Chief Happiness Officer & Emotional Capital Strategist"),
        ("alex_nordstrom", "Alex Nordstrom - Principal AI Prompt Architect & Thought Leader | Top Voice"),
        ("priya_sharma", "Priya Sharma - Director of Cross-Functional Synergies & Sustainable Cloud"),
        ("james_harrison", "James Harrison - Enterprise Growth Ninja & TEDx Keynote Speaker en Liderazgo"),
        ("valerie_leblanc", "Valérie Leblanc - Senior VP of Talent Optimization & High-Vibe Corporate Culture"),
        ("marcus_vance", "Marcus Vance - Disruptive Venture Partner & Angel Investor en DeepTech"),
        ("beatriz_navarro", "Beatriz Navarro - Lead Executive Change Catalyst & Strategic Mindset Mentor"),
        ("guillaume_moreau", "Guillaume Moreau - Co-founder & Chief Visionary Evangelist de Sinergias")
    ];

    private readonly List<ProfileEntry> _profiles = new();
    private readonly Dictionary<Guid, ProfileEntry> _byId = new();
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _profiles.Count; }
    }

    public GameRepository()
    {
        Directory.CreateDirectory("Assets");
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Cargar perfiles escaneados persistidos en JSON para no perder cargos o delitos cómicos
        if (File.Exists(UserProfilesJsonPath))
        {
            try
            {
                var json = File.ReadAllText(UserProfilesJsonPath);
                var savedProfiles = JsonSerializer.Deserialize<List<UserProfileData>>(json);
                if (savedProfiles != null)
                {
                    foreach (var sp in savedProfiles)
                    {
                        if (File.Exists(sp.ImagePath))
                        {
                            AddProfileInternal(sp.ImagePath, sp.Category, sp.Description);
                            loadedPaths.Add(Path.GetFullPath(sp.ImagePath));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al leer {UserProfilesJsonPath}: {ex.Message}");
            }
        }

        // 2. Cargar perfiles estándar de la carpeta Assets
        foreach (var file in Directory.EnumerateFiles("Assets"))
        {
            if (!ValidExtensions.Contains(Path.GetExtension(file)))
                continue;

            string fullPath = Path.GetFullPath(file);
            if (loadedPaths.Contains(fullPath))
                continue;

            string cat = file.Contains("interpol", StringComparison.OrdinalIgnoreCase) ? "Interpol" : "LinkedIn";
            string desc = GetCustomDescription(file, cat);
            AddProfileInternal(file, cat, desc);
            loadedPaths.Add(fullPath);
        }

        if (_profiles.Count == 0)
        {
            CreatePlaceholder("Assets/linkedin_demo.jpg", new Scalar(180, 100, 20), "LinkedIn");
            CreatePlaceholder("Assets/interpol_demo.jpg", new Scalar(40, 40, 180), "Interpol");
        }

        if (!File.Exists(UserProfilesJsonPath))
        {
            SaveUserProfiles();
        }
    }

    private static string GetCustomDescription(string filePath, string category)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        foreach (var (key, desc) in KnownDescriptions)
        {
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
                return desc;
        }
        return category == "Interpol" ? "Notificación Roja de Interpol" : "Perfil Ejecutivo de LinkedIn";
    }

    private void CreatePlaceholder(string path, Scalar color, string category)
    {
        using var mat = new Mat(400, 400, MatType.CV_8UC3, color);
        Cv2.PutText(mat, $"Demo: {category}", new Point(60, 200), HersheyFonts.HersheySimplex, 1.0, Scalar.White, 2);
        Cv2.ImWrite(path, mat);
        AddProfileInternal(path, category, $"Perfil de prueba ({category})");
    }

    private void AddProfileInternal(string path, string category, string description)
    {
        var entry = new ProfileEntry(Guid.NewGuid(), path, category, description);
        _profiles.Add(entry);
        _byId[entry.Id] = entry;
    }

    public ProfileEntry GetRandomProfile(Guid? excludeId = null)
    {
        lock (_lock)
        {
            if (_profiles.Count <= 1 || excludeId is null)
                return _profiles[Random.Shared.Next(_profiles.Count)];

            int idx = Random.Shared.Next(_profiles.Count);
            if (_profiles[idx].Id == excludeId.Value)
                idx = (idx + 1) % _profiles.Count;

            return _profiles[idx];
        }
    }

    public ProfileEntry? GetById(Guid id)
    {
        lock (_lock)
        {
            return _byId.GetValueOrDefault(id);
        }
    }

    public void AddProfile(string path, string category, string description)
    {
        lock (_lock)
        {
            AddProfileInternal(path, category, description);
            SaveUserProfiles();
        }
    }

    private void SaveUserProfiles()
    {
        try
        {
            var userProfiles = _profiles
                .Where(p => Path.GetFileName(p.ImagePath).StartsWith("user_", StringComparison.OrdinalIgnoreCase))
                .Select(p => new UserProfileData(p.ImagePath, p.Category, p.Description, DateTime.UtcNow))
                .ToList();

            var json = JsonSerializer.Serialize(userProfiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserProfilesJsonPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar {UserProfilesJsonPath}: {ex.Message}");
        }
    }

    // ================= LEADERBOARD / RANKING =================
    public List<ScoreEntry> GetLeaderboard()
    {
        lock (_lock)
        {
            if (!File.Exists(LeaderboardJsonPath))
                return new List<ScoreEntry>();

            try
            {
                var json = File.ReadAllText(LeaderboardJsonPath);
                return JsonSerializer.Deserialize<List<ScoreEntry>>(json) ?? new List<ScoreEntry>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al leer {LeaderboardJsonPath}: {ex.Message}");
                return new List<ScoreEntry>();
            }
        }
    }

    public List<ScoreEntry> AddScore(ScoreRequest req)
    {
        lock (_lock)
        {
            var scores = GetLeaderboard();
            var name = string.IsNullOrWhiteSpace(req.PlayerName) ? "Anónimo" : req.PlayerName.Trim();
            if (name.Length > 25) name = name[..25];

            scores.Add(new ScoreEntry(
                name,
                Math.Max(0, req.Points),
                Math.Clamp(req.Accuracy, 0, 100),
                Math.Max(0, req.MaxStreak),
                DateTime.UtcNow
            ));

            var top10 = scores
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.Accuracy)
                .Take(10)
                .ToList();

            try
            {
                var json = JsonSerializer.Serialize(top10, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LeaderboardJsonPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar {LeaderboardJsonPath}: {ex.Message}");
            }

            return top10;
        }
    }
}
