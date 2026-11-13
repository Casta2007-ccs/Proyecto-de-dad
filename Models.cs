public record ProfileEntry(Guid Id, string ImagePath, string Category, string Description);

public record GuessRequest(Guid Id, string Guess);

public record ScoreRequest(string PlayerName, int Points, int Accuracy, int MaxStreak);

public record ScoreEntry(string PlayerName, int Points, int Accuracy, int MaxStreak, DateTime Date);

public record UserProfileData(string ImagePath, string Category, string Description, DateTime CreatedAt);

public record BiometricTelemetry(
    int SmileScore,
    int SymmetryScore,
    int BrightnessScore,
    int SharpnessScore,
    int EyesDetected,
    bool HasSmile
);

public record ScanResult(
    bool Success,
    string Classification,
    string Description,
    int LinkedInProbability,
    byte[] AnnotatedImage,
    BiometricTelemetry? Telemetry
);

public record FaceControlResult(
    bool FaceDetected,
    string Action,
    bool IsProfile,
    int BoxX,
    int BoxY,
    int BoxW,
    int BoxH,
    double NormX,
    double NormY,
    double NormW,
    double NormH,
    byte[]? AnnotatedImage
);
