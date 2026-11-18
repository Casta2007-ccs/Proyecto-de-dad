Public Class ProfileEntry
    Public Property Id As Guid
    Public Property ImagePath As String = ""
    Public Property Category As String = ""
    Public Property Description As String = ""

    Public Sub New()
    End Sub

    Public Sub New(id As Guid, imagePath As String, category As String, description As String)
        Me.Id = id
        Me.ImagePath = imagePath
        Me.Category = category
        Me.Description = description
    End Sub
End Class

Public Class ScoreRequest
    Public Property PlayerName As String = ""
    Public Property Points As Integer
    Public Property Accuracy As Integer
    Public Property MaxStreak As Integer
End Class

Public Class ScoreEntry
    Public Property PlayerName As String = ""
    Public Property Points As Integer
    Public Property Accuracy As Integer
    Public Property MaxStreak As Integer
    Public Property [Date] As DateTime
End Class

Public Class UserProfileData
    Public Property ImagePath As String = ""
    Public Property Category As String = ""
    Public Property Description As String = ""
    Public Property CreatedAt As DateTime
End Class

Public Class BiometricTelemetry
    Public Property SmileScore As Integer
    Public Property SymmetryScore As Integer
    Public Property BrightnessScore As Integer
    Public Property SharpnessScore As Integer
    Public Property EyesDetected As Integer
    Public Property HasSmile As Boolean
End Class

Public Class ScanResult
    Public Property Success As Boolean
    Public Property Classification As String = ""
    Public Property Description As String = ""
    Public Property LinkedInProbability As Integer
    Public Property AnnotatedImage As Byte() = Array.Empty(Of Byte)()
    Public Property Telemetry As BiometricTelemetry
End Class

Public Class FaceControlResult
    Public Property FaceDetected As Boolean
    Public Property Action As String = "Center"
    Public Property IsProfile As Boolean
    Public Property NormX As Double
    Public Property NormY As Double
    Public Property NormW As Double
    Public Property NormH As Double
End Class
