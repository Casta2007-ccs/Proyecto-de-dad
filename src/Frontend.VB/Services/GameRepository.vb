Imports System.IO
Imports System.Text.Json
Imports Cv = OpenCvSharp

Public Class GameRepository
    Private ReadOnly _dataRoot As String
    Private ReadOnly _assetsDir As String
    Private ReadOnly _userProfilesJsonPath As String
    Private ReadOnly _leaderboardJsonPath As String

    Private Shared ReadOnly ValidExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".jpg", ".jpeg", ".png", ".webp"
    }

    Private Shared ReadOnly KnownDescriptions As (Key As String, Description As String)() = {
        ("ruja_ignatova", "Ruja Ignatova - 'CryptoQueen', buscada por Interpol/FBI por la megaestafa OneCoin ($4B)"),
        ("do_kwon", "Do Kwon - Notificación Roja de Interpol tras el colapso de Terra/Luna ($40B)"),
        ("semion_mogilevich", "Semion Mogilevich - En la lista de los más buscados por crimen organizado transnacional"),
        ("carlos_ghosn", "Carlos Ghosn - Notificación Roja de Interpol tras su fuga de Japón en una caja de audio"),
        ("bernie_madoff", "Bernie Madoff - Autor del mayor esquema Ponzi de la historia financiera ($64.8B)"),
        ("elizabeth_holmes", "Elizabeth Holmes - Condenada por fraude masivo en Theranos"),
        ("sam_bankman_fried", "Sam Bankman-Fried - Condenado por fraude y desvío multimillonario de fondos en FTX"),
        ("martin_shkreli", "Martin Shkreli - Condenado por fraude de valores y conspiración financiera"),
        ("ross_ulbricht", "Ross Ulbricht - Creador del mercado clandestino Silk Road en la dark web"),
        ("jordan_belfort", "Jordan Belfort - 'El Lobo de Wall Street', condenado por fraude bursátil y blanqueo"),
        ("frank_abagnale", "Frank Abagnale Jr. - Célebre falsificador de cheques y suplantador de identidad"),
        ("viktor_bout", "Viktor Bout - Conocido como 'El Mercader de la Muerte', tráfico internacional de armas"),
        ("satya_nadella", "Satya Nadella - CEO de Microsoft"),
        ("sundar_pichai", "Sundar Pichai - CEO de Google y Alphabet"),
        ("tim_cook", "Tim Cook - CEO de Apple"),
        ("jensen_huang", "Jensen Huang - Cofundador y CEO de NVIDIA"),
        ("lisa_su", "Lisa Su - Presidenta y CEO de AMD"),
        ("sam_altman", "Sam Altman - CEO de OpenAI"),
        ("marc_benioff", "Marc Benioff - Cofundador y CEO de Salesforce"),
        ("sheryl_sandberg", "Sheryl Sandberg - Ex-COO de Meta y fundadora de Lean In"),
        ("reid_hoffman", "Reid Hoffman - Cofundador de LinkedIn y socio en Greylock"),
        ("whitney_wolfe_herd", "Whitney Wolfe Herd - Fundadora de Bumble"),
        ("demis_hassabis", "Demis Hassabis - Cofundador y CEO de Google DeepMind (Premio Nobel)"),
        ("reed_hastings", "Reed Hastings - Cofundador y presidente ejecutivo de Netflix"),
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
    }

    Private ReadOnly _profiles As New List(Of ProfileEntry)()
    Private ReadOnly _byId As New Dictionary(Of Guid, ProfileEntry)()
    Private ReadOnly _lock As New Object()

    Public ReadOnly Property DataRoot As String
        Get
            Return _dataRoot
        End Get
    End Property

    Public ReadOnly Property AssetsDir As String
        Get
            Return _assetsDir
        End Get
    End Property

    Public ReadOnly Property Count As Integer
        Get
            SyncLock _lock
                Return _profiles.Count
            End SyncLock
        End Get
    End Property

    Public Sub New()
        _dataRoot = ResolveDataRoot()
        _assetsDir = Path.Combine(_dataRoot, "Assets")
        _userProfilesJsonPath = Path.Combine(_assetsDir, "user_profiles.json")
        _leaderboardJsonPath = Path.Combine(_dataRoot, "leaderboard.json")

        Directory.CreateDirectory(_assetsDir)
        Dim loadedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        If File.Exists(_userProfilesJsonPath) Then
            Try
                Dim json = File.ReadAllText(_userProfilesJsonPath)
                Dim saved = JsonSerializer.Deserialize(Of List(Of UserProfileData))(json)
                If saved IsNot Nothing Then
                    For Each sp In saved
                        Dim resolved = If(Path.IsPathRooted(sp.ImagePath), sp.ImagePath, Path.Combine(_dataRoot, sp.ImagePath))
                        If File.Exists(resolved) Then
                            AddProfileInternal(resolved, sp.Category, sp.Description)
                            loadedPaths.Add(Path.GetFullPath(resolved))
                        End If
                    Next
                End If
            Catch
            End Try
        End If

        For Each file In Directory.EnumerateFiles(_assetsDir)
            If Not ValidExtensions.Contains(Path.GetExtension(file)) Then Continue For
            Dim fullPath = Path.GetFullPath(file)
            If loadedPaths.Contains(fullPath) Then Continue For

            Dim cat = If(file.Contains("interpol", StringComparison.OrdinalIgnoreCase), "Interpol", "LinkedIn")
            Dim desc = GetCustomDescription(file, cat)
            AddProfileInternal(file, cat, desc)
            loadedPaths.Add(fullPath)
        Next

        If _profiles.Count = 0 Then
            CreatePlaceholder(Path.Combine(_assetsDir, "linkedin_demo.jpg"), New Cv.Scalar(180, 100, 20), "LinkedIn")
            CreatePlaceholder(Path.Combine(_assetsDir, "interpol_demo.jpg"), New Cv.Scalar(40, 40, 180), "Interpol")
        End If
    End Sub

    Public Shared Function ResolveDataRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim dataDir = Path.Combine(dir.FullName, "Data")
            If Directory.Exists(Path.Combine(dataDir, "Assets")) OrElse
               Directory.Exists(Path.Combine(dataDir, "Cascades")) Then
                Return dataDir
            End If
            dir = dir.Parent
        End While
        Return Path.Combine(Directory.GetCurrentDirectory(), "Data")
    End Function

    Private Shared Function GetCustomDescription(filePath As String, category As String) As String
        Dim name = Path.GetFileNameWithoutExtension(filePath)
        For Each item In KnownDescriptions
            If name.Contains(item.Key, StringComparison.OrdinalIgnoreCase) Then
                Return item.Description
            End If
        Next
        Return If(category = "Interpol", "Notificación Roja de Interpol", "Perfil Ejecutivo de LinkedIn")
    End Function

    Private Sub CreatePlaceholder(path As String, color As Cv.Scalar, category As String)
        Using mat As New Cv.Mat(400, 400, Cv.MatType.CV_8UC3, color)
            Cv.Cv2.PutText(mat, $"Demo: {category}", New Cv.Point(60, 200), Cv.HersheyFonts.HersheySimplex, 1.0, Cv.Scalar.White, 2)
            Cv.Cv2.ImWrite(path, mat)
        End Using
        AddProfileInternal(path, category, $"Perfil de prueba ({category})")
    End Sub

    Private Sub AddProfileInternal(path As String, category As String, description As String)
        Dim entry As New ProfileEntry(Guid.NewGuid(), path, category, description)
        _profiles.Add(entry)
        _byId(entry.Id) = entry
    End Sub

    Public Function GetRandomProfile(Optional excludeId As Guid? = Nothing) As ProfileEntry
        SyncLock _lock
            If _profiles.Count <= 1 OrElse Not excludeId.HasValue Then
                Return _profiles(Random.Shared.Next(_profiles.Count))
            End If

            Dim idx = Random.Shared.Next(_profiles.Count)
            If _profiles(idx).Id = excludeId.Value Then
                idx = (idx + 1) Mod _profiles.Count
            End If
            Return _profiles(idx)
        End SyncLock
    End Function

    Public Function GetById(id As Guid) As ProfileEntry
        SyncLock _lock
            Dim entry As ProfileEntry = Nothing
            _byId.TryGetValue(id, entry)
            Return entry
        End SyncLock
    End Function

    Public Sub AddProfile(path As String, category As String, description As String)
        SyncLock _lock
            AddProfileInternal(path, category, description)
            SaveUserProfiles()
        End SyncLock
    End Sub

    Private Sub SaveUserProfiles()
        Try
            Dim userProfiles = _profiles.
                Where(Function(p) Path.GetFileName(p.ImagePath).StartsWith("user_", StringComparison.OrdinalIgnoreCase)).
                Select(Function(p) New UserProfileData With {
                    .ImagePath = Path.GetRelativePath(_dataRoot, p.ImagePath).Replace("\"c, "/"c),
                    .Category = p.Category,
                    .Description = p.Description,
                    .CreatedAt = DateTime.UtcNow
                }).
                ToList()

            Dim json = JsonSerializer.Serialize(userProfiles, New JsonSerializerOptions With {.WriteIndented = True})
            File.WriteAllText(_userProfilesJsonPath, json)
        Catch
        End Try
    End Sub

    Public Function GetLeaderboard() As List(Of ScoreEntry)
        SyncLock _lock
            If Not File.Exists(_leaderboardJsonPath) Then Return New List(Of ScoreEntry)()
            Try
                Dim json = File.ReadAllText(_leaderboardJsonPath)
                Return If(JsonSerializer.Deserialize(Of List(Of ScoreEntry))(json), New List(Of ScoreEntry)())
            Catch
                Return New List(Of ScoreEntry)()
            End Try
        End SyncLock
    End Function

    Public Function AddScore(req As ScoreRequest) As List(Of ScoreEntry)
        SyncLock _lock
            Dim scores = GetLeaderboard()
            Dim name = If(String.IsNullOrWhiteSpace(req.PlayerName), "Anónimo", req.PlayerName.Trim())
            If name.Length > 25 Then name = name.Substring(0, 25)

            scores.Add(New ScoreEntry With {
                .PlayerName = name,
                .Points = Math.Max(0, req.Points),
                .Accuracy = Math.Clamp(req.Accuracy, 0, 100),
                .MaxStreak = Math.Max(0, req.MaxStreak),
                .Date = DateTime.UtcNow
            })

            Dim top10 = scores.
                OrderByDescending(Function(s) s.Points).
                ThenByDescending(Function(s) s.Accuracy).
                Take(10).
                ToList()

            Try
                Dim json = JsonSerializer.Serialize(top10, New JsonSerializerOptions With {.WriteIndented = True})
                File.WriteAllText(_leaderboardJsonPath, json)
            Catch
            End Try

            Return top10
        End SyncLock
    End Function
End Class
