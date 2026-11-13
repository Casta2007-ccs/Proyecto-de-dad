using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<GameRepository>();
builder.Services.AddSingleton<OpenCvService>();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
// "no-cache" + ETag: el navegador revalida y recibe 304 si los archivos no cambiaron
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache"
});

app.Services.GetRequiredService<GameRepository>();
await app.Services.GetRequiredService<OpenCvService>().EnsureCascadesAsync();

// 1. Obtener ronda aleatoria (evitando repetir el perfil inmediato anterior)
app.MapGet("/api/round", ([FromQuery] Guid? exclude, GameRepository repo) =>
{
    var profile = repo.GetRandomProfile(exclude);
    return Results.Ok(new { id = profile.Id, totalProfiles = repo.Count });
});

// 2. Servir retrato procesado por OpenCV con caché en servidor y navegador
app.MapGet("/api/image/{id:guid}", (
    Guid id,
    [FromQuery] bool grayscale,
    [FromQuery] bool blur,
    HttpContext ctx,
    GameRepository repo,
    OpenCvService cv) =>
{
    var profile = repo.GetById(id);
    if (profile is null || !File.Exists(profile.ImagePath))
        return Results.NotFound();

    ctx.Response.Headers.CacheControl = "public, max-age=3600";
    var bytes = cv.ProcessGamePortrait(profile.ImagePath, grayscale, blur);
    return Results.File(bytes, "image/jpeg");
});

// 3. Verificar respuesta
app.MapPost("/api/guess", ([FromBody] GuessRequest req, GameRepository repo) =>
{
    var profile = repo.GetById(req.Id);
    if (profile is null) return Results.NotFound();

    bool isCorrect = string.Equals(profile.Category, req.Guess, StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new
    {
        correct = isCorrect,
        actualCategory = profile.Category,
        description = profile.Description
    });
});

// 4. Controlar el juego con la cara. Acepta el JPEG en bruto (sin multipart) para mínima sobrecarga.
app.MapPost("/api/face-control", async (HttpContext ctx, OpenCvService cv) =>
{
    var rawBytes = await ReadImageAsync(ctx.Request, "frame");
    if (rawBytes is null) return Results.BadRequest();

    bool annotate = ctx.Request.Query["annotate"] == "true";

    var control = cv.DetectFaceControl(rawBytes, annotate);
    return Results.Ok(new
    {
        faceDetected = control.FaceDetected,
        action = control.Action,
        box = new
        {
            x = control.BoxX,
            y = control.BoxY,
            width = control.BoxW,
            height = control.BoxH,
            normX = control.NormX,
            normY = control.NormY,
            normWidth = control.NormW,
            normHeight = control.NormH
        },
        profileAngle = control.IsProfile,
        annotatedFrame = control.AnnotatedImage is { Length: > 0 } img
            ? $"data:image/jpeg;base64,{Convert.ToBase64String(img)}"
            : null
    });
});

// 5. Escaneo biométrico con telemetría facial
app.MapPost("/api/biometric-scan", async (HttpContext ctx, OpenCvService cv, GameRepository repo) =>
{
    var rawBytes = await ReadImageAsync(ctx.Request, "image");
    if (rawBytes is null) return Results.BadRequest();

    var scan = cv.AnalyzeAndCatalogFace(rawBytes);
    if (!scan.Success)
        return Results.Ok(new { success = false, message = scan.Description });

    Directory.CreateDirectory("Assets");
    string tag = scan.Classification.ToLowerInvariant();
    string fileName = $"Assets/user_{tag}_{Guid.NewGuid():N}.jpg";
    await File.WriteAllBytesAsync(fileName, rawBytes);
    repo.AddProfile(fileName, scan.Classification, scan.Description);

    return Results.Ok(new
    {
        success = true,
        classification = scan.Classification,
        description = scan.Description,
        probability = scan.LinkedInProbability,
        imageData = $"data:image/jpeg;base64,{Convert.ToBase64String(scan.AnnotatedImage)}",
        telemetry = scan.Telemetry
    });
});

// 6. Obtener Leaderboard (Top 10 puntuaciones)
app.MapGet("/api/leaderboard", (GameRepository repo) =>
{
    return Results.Ok(repo.GetLeaderboard());
});

// 7. Guardar nueva puntuación en Leaderboard
app.MapPost("/api/scores", ([FromBody] ScoreRequest req, GameRepository repo) =>
{
    var leaderboard = repo.AddScore(req);
    return Results.Ok(new { success = true, leaderboard });
});

app.Run();

// Lee una imagen enviada como cuerpo binario (image/jpeg) o como campo multipart. Límite 5 MB.
static async Task<byte[]?> ReadImageAsync(HttpRequest req, string formField)
{
    const long MaxBytes = 5 * 1024 * 1024;

    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile(formField);
        if (file is null || file.Length == 0 || file.Length > MaxBytes) return null;

        var buffer = new byte[file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(buffer);
        return buffer;
    }

    if (req.ContentLength is long len and > 0 and <= MaxBytes)
    {
        var buffer = new byte[len];
        await req.Body.ReadExactlyAsync(buffer);
        return buffer;
    }

    // Sin Content-Length (chunked): copia acotada
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    return ms.Length is > 0 and <= MaxBytes ? ms.ToArray() : null;
}
