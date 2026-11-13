using System.Collections.Concurrent;
using OpenCvSharp;

public sealed class OpenCvService : IDisposable
{
    private const string FaceAlt2Xml = "haarcascade_frontalface_alt2.xml";
    private const string FaceDefaultXml = "haarcascade_frontalface_default.xml";
    private const string ProfileXml = "haarcascade_profileface.xml";
    private const string EyeXml = "haarcascade_eye.xml";
    private const string SmileXml = "haarcascade_smile.xml";

    private static readonly ImageEncodingParam[] JpegQuality88 = [new(ImwriteFlags.JpegQuality, 88)];
    private static readonly ImageEncodingParam[] JpegQuality75 = [new(ImwriteFlags.JpegQuality, 75)];
    private static readonly byte[] FallbackPortraitBytes = CreateFallbackPortrait();

    private static readonly string[] LinkedInRoles =
    [
        "CEO de Sinergias Disruptivas & Evangelista Web3",
        "VP de Transformación Ágil y Felicidad Corporativa",
        "Senior Prompt Architect & Mentor de Alto Impacto",
        "Director Global de Optimización de Métricas Cuánticas",
        "Chief AI Mindset Officer & TEDx Keynote Speaker"
    ];

    private static readonly string[] InterpolCrimes =
    [
        "Notificación Roja: Subir código a producción un viernes a las 19:55",
        "Buscado internacionalmente por evasión de reuniones diarias y robo de tuppers",
        "Prófugo cibernético: Borrar la base de datos sin hacer backup",
        "Crimen tecnológico: Indentar código mezclando espacios y tabuladores",
        "Evasión internacional: Silenciar el micrófono y fingir problemas de conexión"
    ];

    private CascadeClassifier? _faceCascadeAlt2;
    private CascadeClassifier? _faceCascadeDefault;
    private CascadeClassifier? _profileCascade;
    private CascadeClassifier? _eyeCascade;
    private CascadeClassifier? _smileCascade;

    // CascadeClassifier nativo en OpenCV no es thread-safe para llamadas simultáneas
    private readonly object _cascadeLock = new();
    // CLAHE reutilizable (se usa siempre dentro de _cascadeLock), evita crear un objeto nativo por fotograma
    private readonly CLAHE _clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
    private readonly ConcurrentDictionary<string, byte[]> _baseCropCache = new();
    private readonly ConcurrentDictionary<string, byte[]> _portraitCache = new();

    private static byte[] CreateFallbackPortrait()
    {
        using var fallback = new Mat(400, 400, MatType.CV_8UC3, new Scalar(60, 60, 60));
        Cv2.PutText(fallback, "Sin Imagen", new Point(100, 200), HersheyFonts.HersheySimplex, 1.0, Scalar.White, 2);
        Cv2.ImEncode(".jpg", fallback, out byte[] buf, JpegQuality88);
        return buf;
    }

    public async Task EnsureCascadesAsync()
    {
        string[] files = [FaceAlt2Xml, FaceDefaultXml, ProfileXml, EyeXml, SmileXml];
        if (files.Any(f => !File.Exists(f)))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            await Task.WhenAll(files.Where(f => !File.Exists(f)).Select(async fileName =>
            {
                var bytes = await http.GetByteArrayAsync($"https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/{fileName}");
                await File.WriteAllBytesAsync(fileName, bytes);
            }));
        }

        _faceCascadeAlt2 = new CascadeClassifier(FaceAlt2Xml);
        _faceCascadeDefault = new CascadeClassifier(FaceDefaultXml);
        _profileCascade = new CascadeClassifier(ProfileXml);
        _eyeCascade = new CascadeClassifier(EyeXml);
        _smileCascade = new CascadeClassifier(SmileXml);
    }

    // Servir retrato del juego reutilizando el recorte base 400x400 para filtros instantáneos
    public byte[] ProcessGamePortrait(string imagePath, bool grayscale, bool blur)
    {
        string cacheKey = $"{imagePath}_{grayscale}_{blur}";
        if (_portraitCache.TryGetValue(cacheKey, out var cachedBytes))
            return cachedBytes;

        var baseBytes = GetOrCreateBaseCroppedPortrait(imagePath);
        if (baseBytes.Length == 0)
            return FallbackPortraitBytes;

        if (!grayscale && !blur)
        {
            _portraitCache[cacheKey] = baseBytes;
            return baseBytes;
        }

        using var resized = Cv2.ImDecode(baseBytes, ImreadModes.Color);
        if (resized.Empty())
            return FallbackPortraitBytes;

        if (grayscale)
        {
            Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(resized, resized, ColorConversionCodes.GRAY2BGR);
        }

        if (blur)
            Cv2.GaussianBlur(resized, resized, new Size(15, 15), 0);

        Cv2.ImEncode(".jpg", resized, out byte[] buffer, JpegQuality88);
        _portraitCache[cacheKey] = buffer;
        return buffer;
    }

    private byte[] GetOrCreateBaseCroppedPortrait(string imagePath)
    {
        if (_baseCropCache.TryGetValue(imagePath, out var cached))
            return cached;

        using var src = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (src.Empty())
            return Array.Empty<byte>();

        Rect cropRect;
        using (var gray = new Mat())
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            Rect[] faces;
            lock (_cascadeLock)
            {
                faces = _faceCascadeAlt2?.DetectMultiScale(gray, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(26, 26)) ?? [];
                if (faces.Length == 0 && _faceCascadeDefault is not null)
                    faces = _faceCascadeDefault.DetectMultiScale(gray, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(26, 26));
            }

            if (faces.Length > 0)
            {
                var f = GetLargestRect(faces);
                int mx = (int)(f.Width * 0.35), my = (int)(f.Height * 0.35);
                int x = Math.Max(0, f.X - mx), y = Math.Max(0, f.Y - my);
                int w = Math.Min(src.Width - x, f.Width + mx * 2), h = Math.Min(src.Height - y, f.Height + my * 2);
                cropRect = new Rect(x, y, w, h);
            }
            else
            {
                cropRect = new Rect(0, 0, src.Width, src.Height);
            }
        }

        using var cropped = new Mat(src, cropRect);
        using var resized = new Mat();
        Cv2.Resize(cropped, resized, new Size(400, 400), 0, 0, InterpolationFlags.Linear);
        Cv2.ImEncode(".jpg", resized, out byte[] baseBytes, JpegQuality88);

        _baseCropCache[imagePath] = baseBytes;
        return baseBytes;
    }

    public FaceControlResult DetectFaceControl(byte[] frameBytes, bool annotate = false)
    {
        using var gray = Cv2.ImDecode(frameBytes, ImreadModes.Grayscale);
        if (gray.Empty())
            return new FaceControlResult(false, "Center", false, 0, 0, 0, 0, 0, 0, 0, 0, null);

        int origW = gray.Width;
        int origH = gray.Height;

        double scale = 1.0;
        using var smallGray = new Mat();
        if (origW > 200)
        {
            scale = 200.0 / origW;
            Cv2.Resize(gray, smallGray, new Size(200, (int)(origH * scale)), 0, 0, InterpolationFlags.Nearest);
        }
        else
        {
            gray.CopyTo(smallGray);
        }

        bool isProfile = false;
        Rect[] faces;

        lock (_cascadeLock)
        {
            _clahe.Apply(smallGray, smallGray);
            faces = _faceCascadeAlt2?.DetectMultiScale(smallGray, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(22, 22)) ?? [];

            if (faces.Length == 0 && _faceCascadeDefault is not null)
                faces = _faceCascadeDefault.DetectMultiScale(smallGray, 1.18, 3, HaarDetectionTypes.ScaleImage, new Size(22, 22));

            if (faces.Length == 0 && _profileCascade is not null)
            {
                faces = _profileCascade.DetectMultiScale(smallGray, 1.18, 3, HaarDetectionTypes.ScaleImage, new Size(22, 22));
                if (faces.Length > 0)
                {
                    isProfile = true;
                }
                else
                {
                    using var flipped = new Mat();
                    Cv2.Flip(smallGray, flipped, FlipMode.Y);
                    var flippedFaces = _profileCascade.DetectMultiScale(flipped, 1.18, 3, HaarDetectionTypes.ScaleImage, new Size(22, 22));
                    if (flippedFaces.Length > 0)
                    {
                        isProfile = true;
                        var f = GetLargestRect(flippedFaces);
                        faces = [new Rect(smallGray.Width - f.X - f.Width, f.Y, f.Width, f.Height)];
                    }
                }
            }
        }

        string action = "Center";
        int boxX = 0, boxY = 0, boxW = 0, boxH = 0;
        double normX = 0, normY = 0, normW = 0, normH = 0;

        int leftLimit = (int)(origW * 0.38);
        int rightLimit = (int)(origW * 0.62);

        if (faces.Length > 0)
        {
            var f = GetLargestRect(faces);
            boxX = (int)(f.X / scale);
            boxY = (int)(f.Y / scale);
            boxW = (int)(f.Width / scale);
            boxH = (int)(f.Height / scale);

            normX = (double)boxX / origW;
            normY = (double)boxY / origH;
            normW = (double)boxW / origW;
            normH = (double)boxH / origH;

            int centerX = boxX + boxW / 2;
            if (centerX < leftLimit) action = "LinkedIn";
            else if (centerX > rightLimit) action = "Interpol";
        }

        byte[]? outBuf = null;
        if (annotate)
        {
            using var color = Cv2.ImDecode(frameBytes, ImreadModes.Color);
            if (!color.Empty())
            {
                Cv2.Line(color, new Point(leftLimit, 0), new Point(leftLimit, color.Height), new Scalar(194, 102, 10), 2);
                Cv2.Line(color, new Point(rightLimit, 0), new Point(rightLimit, color.Height), new Scalar(38, 38, 220), 2);

                if (faces.Length > 0)
                {
                    Scalar boxColor = action == "LinkedIn" ? new Scalar(194, 102, 10) :
                                      action == "Interpol" ? new Scalar(38, 38, 220) : Scalar.Green;
                    Cv2.Rectangle(color, new Rect(boxX, boxY, boxW, boxH), boxColor, 3);
                    Cv2.PutText(color, action.ToUpper(), new Point(boxX, Math.Max(20, boxY - 10)),
                        HersheyFonts.HersheySimplex, 0.7, boxColor, 2);
                }
                Cv2.ImEncode(".jpg", color, out outBuf, JpegQuality75);
            }
        }

        return new FaceControlResult(faces.Length > 0, action, isProfile, boxX, boxY, boxW, boxH, normX, normY, normW, normH, outBuf);
    }

    public ScanResult AnalyzeAndCatalogFace(byte[] imageBytes)
    {
        using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (src.Empty())
            return new ScanResult(false, "Ninguno", "No se detectó tu cara. Ponte frente a la cámara.", 0, [], null);

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        Rect[] faces;
        lock (_cascadeLock)
        {
            _clahe.Apply(gray, gray);
            faces = _faceCascadeAlt2?.DetectMultiScale(gray, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(26, 26)) ?? [];
            if (faces.Length == 0 && _faceCascadeDefault is not null)
                faces = _faceCascadeDefault.DetectMultiScale(gray, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(26, 26));
        }

        if (faces.Length == 0)
            return new ScanResult(false, "Ninguno", "No se detectó un rostro nítido. Asegúrate de tener buena luz y mirar a la cámara.", 0, [], null);

        var face = GetLargestRect(faces);
        using var faceRoiGray = new Mat(gray, face);

        var upperHalf = new Rect(0, 0, face.Width, (int)(face.Height * 0.55));
        var lowerHalf = new Rect(0, (int)(face.Height * 0.45), face.Width, (int)(face.Height * 0.55));
        using var eyeRoi = new Mat(faceRoiGray, upperHalf);
        using var smileRoi = new Mat(faceRoiGray, lowerHalf);

        Rect[] eyes;
        Rect[] smiles;
        lock (_cascadeLock)
        {
            eyes = _eyeCascade?.DetectMultiScale(eyeRoi, 1.15, 3, HaarDetectionTypes.ScaleImage, new Size(8, 8)) ?? [];
            smiles = _smileCascade?.DetectMultiScale(smileRoi, 1.35, 6, HaarDetectionTypes.ScaleImage, new Size(12, 12)) ?? [];
        }

        bool isSmiling = smiles.Length > 0;
        int eyeCount = eyes.Length;

        Cv2.MeanStdDev(faceRoiGray, out Scalar mean, out _);
        double brightness = mean.Val0;

        int halfW = face.Width / 2;
        using var leftSide = new Mat(faceRoiGray, new Rect(0, 0, halfW, face.Height));
        using var rightSide = new Mat(faceRoiGray, new Rect(halfW, 0, halfW, face.Height));
        using var rightFlipped = new Mat();
        Cv2.Flip(rightSide, rightFlipped, FlipMode.Y);
        using var diff = new Mat();
        Cv2.Absdiff(leftSide, rightFlipped, diff);
        double avgDiff = Cv2.Mean(diff).Val0;
        int symmetry = Math.Clamp((int)(100 - (avgDiff * 1.5)), 40, 99);

        using var lap = new Mat();
        Cv2.Laplacian(faceRoiGray, lap, MatType.CV_64F);
        Cv2.MeanStdDev(lap, out _, out Scalar lapStd);
        double sharpVar = lapStd.Val0 * lapStd.Val0;
        int sharpness = Math.Clamp((int)(sharpVar / 8.0), 20, 98);

        int linkedinProb = isSmiling ? 75 : 30;
        if (brightness > 95) linkedinProb += 12;
        else if (brightness < 75) linkedinProb -= 15;
        if (symmetry > 75) linkedinProb += 8;
        if (sharpness > 60) linkedinProb += 6;
        if (eyeCount >= 2) linkedinProb += 5;

        linkedinProb = Math.Clamp(linkedinProb + Random.Shared.Next(-5, 6), 5, 98);
        string category = linkedinProb >= 50 ? "LinkedIn" : "Interpol";

        string desc = category == "LinkedIn"
            ? LinkedInRoles[Random.Shared.Next(LinkedInRoles.Length)]
            : InterpolCrimes[Random.Shared.Next(InterpolCrimes.Length)];

        Scalar color = category == "LinkedIn" ? new Scalar(194, 102, 10) : new Scalar(38, 38, 220);
        Cv2.Rectangle(src, face, color, 3);

        foreach (var eye in eyes)
        {
            var eyeCenter = new Point(face.X + eye.X + eye.Width / 2, face.Y + eye.Y + eye.Height / 2);
            Cv2.Circle(src, eyeCenter, Math.Max(eye.Width, eye.Height) / 2, new Scalar(255, 255, 0), 2);
        }

        if (isSmiling && smiles.Length > 0)
        {
            var s = smiles[0];
            var sRect = new Rect(face.X + s.X, face.Y + lowerHalf.Y + s.Y, s.Width, s.Height);
            Cv2.Rectangle(src, sRect, new Scalar(0, 255, 255), 2);
        }

        Cv2.PutText(src, $"{category.ToUpper()} ({linkedinProb}%)", new Point(face.X, Math.Max(25, face.Y - 10)),
            HersheyFonts.HersheySimplex, 0.7, color, 2);

        Cv2.ImEncode(".jpg", src, out byte[] outBuf, JpegQuality88);

        int smileScore = isSmiling ? Math.Clamp(65 + smiles.Length * 12, 60, 99) : Random.Shared.Next(15, 35);
        var telemetry = new BiometricTelemetry(
            SmileScore: smileScore,
            SymmetryScore: symmetry,
            BrightnessScore: Math.Clamp((int)(brightness / 2.55), 10, 99),
            SharpnessScore: sharpness,
            EyesDetected: eyeCount,
            HasSmile: isSmiling
        );

        return new ScanResult(true, category, desc, linkedinProb, outBuf, telemetry);
    }

    private static Rect GetLargestRect(ReadOnlySpan<Rect> rects)
    {
        var best = rects[0];
        int bestArea = best.Width * best.Height;
        for (int i = 1; i < rects.Length; i++)
        {
            int area = rects[i].Width * rects[i].Height;
            if (area > bestArea)
            {
                best = rects[i];
                bestArea = area;
            }
        }
        return best;
    }

    public void Dispose()
    {
        lock (_cascadeLock)
        {
            _faceCascadeAlt2?.Dispose();
            _faceCascadeDefault?.Dispose();
            _profileCascade?.Dispose();
            _eyeCascade?.Dispose();
            _smileCascade?.Dispose();
            _clahe.Dispose();
        }
    }
}
