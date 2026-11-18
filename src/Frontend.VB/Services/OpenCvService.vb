Imports System.Collections.Concurrent
Imports System.IO
Imports System.Net.Http
Imports Cv = OpenCvSharp

Public NotInheritable Class OpenCvService
    Implements IDisposable

    Private Const FaceAlt2Xml As String = "haarcascade_frontalface_alt2.xml"
    Private Const FaceDefaultXml As String = "haarcascade_frontalface_default.xml"
    Private Const ProfileXml As String = "haarcascade_profileface.xml"
    Private Const EyeXml As String = "haarcascade_eye.xml"
    Private Const SmileXml As String = "haarcascade_smile.xml"

    Private Shared ReadOnly JpegQuality88 As Cv.ImageEncodingParam() = {New Cv.ImageEncodingParam(Cv.ImwriteFlags.JpegQuality, 88)}

    Private Shared ReadOnly LinkedInRoles As String() = {
        "CEO de Sinergias Disruptivas & Evangelista Web3",
        "VP de Transformación Ágil y Felicidad Corporativa",
        "Senior Prompt Architect & Mentor de Alto Impacto",
        "Director Global de Optimización de Métricas Cuánticas",
        "Chief AI Mindset Officer & TEDx Keynote Speaker"
    }

    Private Shared ReadOnly InterpolCrimes As String() = {
        "Notificación Roja: Subir código a producción un viernes a las 19:55",
        "Buscado internacionalmente por evasión de reuniones diarias y robo de tuppers",
        "Prófugo cibernético: Borrar la base de datos sin hacer backup",
        "Crimen tecnológico: Indentar código mezclando espacios y tabuladores",
        "Evasión internacional: Silenciar el micrófono y fingir problemas de conexión"
    }

    Private ReadOnly _cascadesDir As String
    Private _faceCascadeAlt2 As Cv.CascadeClassifier
    Private _faceCascadeDefault As Cv.CascadeClassifier
    Private _profileCascade As Cv.CascadeClassifier
    Private _eyeCascade As Cv.CascadeClassifier
    Private _smileCascade As Cv.CascadeClassifier

    Private ReadOnly _cascadeLock As New Object()
    Private ReadOnly _clahe As Cv.CLAHE = Cv.Cv2.CreateCLAHE(2.0, New Cv.Size(8, 8))
    Private ReadOnly _baseCropCache As New ConcurrentDictionary(Of String, Byte())()
    Private ReadOnly _portraitCache As New ConcurrentDictionary(Of String, Byte())()

    Public Sub New(dataRoot As String)
        _cascadesDir = Path.Combine(dataRoot, "Cascades")
        Directory.CreateDirectory(_cascadesDir)
    End Sub

    Public Async Function EnsureCascadesAsync() As Task
        Dim files = {FaceAlt2Xml, FaceDefaultXml, ProfileXml, EyeXml, SmileXml}
        Dim missing = files.Where(Function(f) Not File.Exists(Path.Combine(_cascadesDir, f))).ToArray()
        If missing.Length > 0 Then
            Using http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(20)}
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
                For Each fileName In missing
                    Dim bytes = Await http.GetByteArrayAsync($"https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/{fileName}")
                    Await File.WriteAllBytesAsync(Path.Combine(_cascadesDir, fileName), bytes)
                Next
            End Using
        End If

        _faceCascadeAlt2 = New Cv.CascadeClassifier(Path.Combine(_cascadesDir, FaceAlt2Xml))
        _faceCascadeDefault = New Cv.CascadeClassifier(Path.Combine(_cascadesDir, FaceDefaultXml))
        _profileCascade = New Cv.CascadeClassifier(Path.Combine(_cascadesDir, ProfileXml))
        _eyeCascade = New Cv.CascadeClassifier(Path.Combine(_cascadesDir, EyeXml))
        _smileCascade = New Cv.CascadeClassifier(Path.Combine(_cascadesDir, SmileXml))
    End Function

    Public Function ProcessGamePortrait(imagePath As String, grayscale As Boolean, blur As Boolean) As Byte()
        Dim cacheKey = $"{imagePath}_{grayscale}_{blur}"
        Dim cachedBytes As Byte() = Nothing
        If _portraitCache.TryGetValue(cacheKey, cachedBytes) Then Return cachedBytes

        Dim baseBytes = GetOrCreateBaseCroppedPortrait(imagePath)
        If baseBytes.Length = 0 Then Return CreateFallbackPortrait()

        If Not grayscale AndAlso Not blur Then
            _portraitCache(cacheKey) = baseBytes
            Return baseBytes
        End If

        Using resized = Cv.Cv2.ImDecode(baseBytes, Cv.ImreadModes.Color)
            If resized.Empty() Then Return CreateFallbackPortrait()

            If grayscale Then
                Cv.Cv2.CvtColor(resized, resized, Cv.ColorConversionCodes.BGR2GRAY)
                Cv.Cv2.CvtColor(resized, resized, Cv.ColorConversionCodes.GRAY2BGR)
            End If

            If blur Then
                Cv.Cv2.GaussianBlur(resized, resized, New Cv.Size(15, 15), 0)
            End If

            Dim buffer As Byte() = Nothing
            Cv.Cv2.ImEncode(".jpg", resized, buffer, JpegQuality88)
            _portraitCache(cacheKey) = buffer
            Return buffer
        End Using
    End Function

    Private Shared Function CreateFallbackPortrait() As Byte()
        Using fallback As New Cv.Mat(400, 400, Cv.MatType.CV_8UC3, New Cv.Scalar(60, 60, 60))
            Cv.Cv2.PutText(fallback, "Sin Imagen", New Cv.Point(100, 200), Cv.HersheyFonts.HersheySimplex, 1.0, Cv.Scalar.White, 2)
            Dim buf As Byte() = Nothing
            Cv.Cv2.ImEncode(".jpg", fallback, buf, JpegQuality88)
            Return buf
        End Using
    End Function

    Private Function GetOrCreateBaseCroppedPortrait(imagePath As String) As Byte()
        Dim cached As Byte() = Nothing
        If _baseCropCache.TryGetValue(imagePath, cached) Then Return cached

        Using src = Cv.Cv2.ImRead(imagePath, Cv.ImreadModes.Color)
            If src.Empty() Then Return Array.Empty(Of Byte)()

            Dim cropRect As Cv.Rect
            Using gray As New Cv.Mat()
                Cv.Cv2.CvtColor(src, gray, Cv.ColorConversionCodes.BGR2GRAY)
                Dim faces As Cv.Rect()
                SyncLock _cascadeLock
                    faces = If(_faceCascadeAlt2?.DetectMultiScale(gray, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(26, 26)), Array.Empty(Of Cv.Rect)())
                    If faces.Length = 0 AndAlso _faceCascadeDefault IsNot Nothing Then
                        faces = _faceCascadeDefault.DetectMultiScale(gray, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(26, 26))
                    End If
                End SyncLock

                If faces.Length > 0 Then
                    Dim f = GetLargestRect(faces)
                    Dim mx = CInt(f.Width * 0.35), my = CInt(f.Height * 0.35)
                    Dim x = Math.Max(0, f.X - mx), y = Math.Max(0, f.Y - my)
                    Dim w = Math.Min(src.Width - x, f.Width + mx * 2), h = Math.Min(src.Height - y, f.Height + my * 2)
                    cropRect = New Cv.Rect(x, y, w, h)
                Else
                    cropRect = New Cv.Rect(0, 0, src.Width, src.Height)
                End If
            End Using

            Using cropped As New Cv.Mat(src, cropRect), resized As New Cv.Mat()
                Cv.Cv2.Resize(cropped, resized, New Cv.Size(400, 400), 0, 0, Cv.InterpolationFlags.Linear)
                Dim baseBytes As Byte() = Nothing
                Cv.Cv2.ImEncode(".jpg", resized, baseBytes, JpegQuality88)
                _baseCropCache(imagePath) = baseBytes
                Return baseBytes
            End Using
        End Using
    End Function

    Public Function DetectFaceControl(frameColor As Cv.Mat) As FaceControlResult
        If frameColor Is Nothing OrElse frameColor.Empty() Then
            Return New FaceControlResult()
        End If

        Dim origW = frameColor.Width
        Dim origH = frameColor.Height

        Using gray As New Cv.Mat(), smallGray As New Cv.Mat()
            Cv.Cv2.CvtColor(frameColor, gray, Cv.ColorConversionCodes.BGR2GRAY)
            Dim scale = 1.0
            If origW > 200 Then
                scale = 200.0 / origW
                Cv.Cv2.Resize(gray, smallGray, New Cv.Size(200, CInt(origH * scale)), 0, 0, Cv.InterpolationFlags.Nearest)
            Else
                gray.CopyTo(smallGray)
            End If

            Dim isProfile = False
            Dim faces As Cv.Rect()

            SyncLock _cascadeLock
                _clahe.Apply(smallGray, smallGray)

                faces = If(_faceCascadeAlt2?.DetectMultiScale(smallGray, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(22, 22)), Array.Empty(Of Cv.Rect)())
                If faces.Length = 0 AndAlso _faceCascadeDefault IsNot Nothing Then
                    faces = _faceCascadeDefault.DetectMultiScale(smallGray, 1.18, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(22, 22))
                End If

                If faces.Length = 0 AndAlso _profileCascade IsNot Nothing Then
                    faces = _profileCascade.DetectMultiScale(smallGray, 1.18, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(22, 22))
                    If faces.Length > 0 Then
                        isProfile = True
                    Else
                        Using flipped As New Cv.Mat()
                            Cv.Cv2.Flip(smallGray, flipped, Cv.FlipMode.Y)
                            Dim flippedFaces = _profileCascade.DetectMultiScale(flipped, 1.18, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(22, 22))
                            If flippedFaces.Length > 0 Then
                                isProfile = True
                                Dim f = GetLargestRect(flippedFaces)
                                faces = {New Cv.Rect(smallGray.Width - f.X - f.Width, f.Y, f.Width, f.Height)}
                            End If
                        End Using
                    End If
                End If
            End SyncLock

            Dim res As New FaceControlResult With {
                .FaceDetected = faces.Length > 0,
                .IsProfile = isProfile,
                .Action = "Center"
            }

            If faces.Length > 0 Then
                Dim f = GetLargestRect(faces)
                Dim boxX = CInt(f.X / scale)
                Dim boxY = CInt(f.Y / scale)
                Dim boxW = CInt(f.Width / scale)
                Dim boxH = CInt(f.Height / scale)

                res.NormX = boxX / CDbl(origW)
                res.NormY = boxY / CDbl(origH)
                res.NormW = boxW / CDbl(origW)
                res.NormH = boxH / CDbl(origH)

                Dim centerX = boxX + boxW \ 2
                If centerX < CInt(origW * 0.38) Then
                    res.Action = "LinkedIn"
                ElseIf centerX > CInt(origW * 0.62) Then
                    res.Action = "Interpol"
                End If
            End If

            Return res
        End Using
    End Function

    Public Function AnalyzeAndCatalogFace(rawImage As Cv.Mat) As ScanResult
        If rawImage Is Nothing OrElse rawImage.Empty() Then
            Return New ScanResult With {.Success = False, .Description = "No se detectó tu cara. Ponte frente a la cámara."}
        End If

        Using src = rawImage.Clone(), gray As New Cv.Mat()
            Cv.Cv2.CvtColor(src, gray, Cv.ColorConversionCodes.BGR2GRAY)

            Dim faces As Cv.Rect()
            SyncLock _cascadeLock
                _clahe.Apply(gray, gray)
                faces = If(_faceCascadeAlt2?.DetectMultiScale(gray, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(26, 26)), Array.Empty(Of Cv.Rect)())
                If faces.Length = 0 AndAlso _faceCascadeDefault IsNot Nothing Then
                    faces = _faceCascadeDefault.DetectMultiScale(gray, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(26, 26))
                End If
            End SyncLock

            If faces.Length = 0 Then
                Return New ScanResult With {.Success = False, .Description = "No se detectó un rostro nítido. Asegúrate de tener buena luz y mirar a la cámara."}
            End If

            Dim face = GetLargestRect(faces)
            Using faceRoiGray As New Cv.Mat(gray, face)
                Dim upperHalf As New Cv.Rect(0, 0, face.Width, CInt(face.Height * 0.55))
                Dim lowerHalf As New Cv.Rect(0, CInt(face.Height * 0.45), face.Width, CInt(face.Height * 0.55))
                Using eyeRoi As New Cv.Mat(faceRoiGray, upperHalf), smileRoi As New Cv.Mat(faceRoiGray, lowerHalf)
                    Dim eyes As Cv.Rect()
                    Dim smiles As Cv.Rect()
                    SyncLock _cascadeLock
                        eyes = If(_eyeCascade?.DetectMultiScale(eyeRoi, 1.15, 3, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(8, 8)), Array.Empty(Of Cv.Rect)())
                        smiles = If(_smileCascade?.DetectMultiScale(smileRoi, 1.35, 6, Cv.HaarDetectionTypes.ScaleImage, New Cv.Size(12, 12)), Array.Empty(Of Cv.Rect)())
                    End SyncLock

                    Dim isSmiling = smiles.Length > 0
                    Dim eyeCount = eyes.Length

                    Dim mean As Cv.Scalar, stddev As Cv.Scalar
                    Cv.Cv2.MeanStdDev(faceRoiGray, mean, stddev)
                    Dim brightness = mean.Val0

                    Dim halfW = face.Width \ 2
                    Dim symmetry = 70
                    If halfW > 0 Then
                        Using leftSide As New Cv.Mat(faceRoiGray, New Cv.Rect(0, 0, halfW, face.Height)),
                              rightSide As New Cv.Mat(faceRoiGray, New Cv.Rect(halfW, 0, halfW, face.Height)),
                              rightFlipped As New Cv.Mat(),
                              diff As New Cv.Mat()
                            Cv.Cv2.Flip(rightSide, rightFlipped, Cv.FlipMode.Y)
                            Cv.Cv2.Absdiff(leftSide, rightFlipped, diff)
                            Dim avgDiff = Cv.Cv2.Mean(diff).Val0
                            symmetry = Math.Clamp(CInt(100 - avgDiff * 1.5), 40, 99)
                        End Using
                    End If

                    Dim sharpness As Integer
                    Using lap As New Cv.Mat()
                        Cv.Cv2.Laplacian(faceRoiGray, lap, Cv.MatType.CV_64F)
                        Dim lapMean As Cv.Scalar, lapStd As Cv.Scalar
                        Cv.Cv2.MeanStdDev(lap, lapMean, lapStd)
                        sharpness = Math.Clamp(CInt((lapStd.Val0 * lapStd.Val0) / 8.0), 20, 98)
                    End Using

                    Dim linkedinProb = If(isSmiling, 75, 30)
                    If brightness > 95 Then
                        linkedinProb += 12
                    ElseIf brightness < 75 Then
                        linkedinProb -= 15
                    End If
                    If symmetry > 75 Then linkedinProb += 8
                    If sharpness > 60 Then linkedinProb += 6
                    If eyeCount >= 2 Then linkedinProb += 5
                    linkedinProb = Math.Clamp(linkedinProb + Random.Shared.Next(-5, 6), 5, 98)

                    Dim category = If(linkedinProb >= 50, "LinkedIn", "Interpol")
                    Dim desc = If(category = "LinkedIn",
                        LinkedInRoles(Random.Shared.Next(LinkedInRoles.Length)),
                        InterpolCrimes(Random.Shared.Next(InterpolCrimes.Length)))

                    Dim color = If(category = "LinkedIn", New Cv.Scalar(194, 102, 10), New Cv.Scalar(38, 38, 220))
                    Cv.Cv2.Rectangle(src, face, color, 3)

                    For Each eye In eyes
                        Dim eyeCenter As New Cv.Point(face.X + eye.X + eye.Width \ 2, face.Y + eye.Y + eye.Height \ 2)
                        Cv.Cv2.Circle(src, eyeCenter, Math.Max(eye.Width, eye.Height) \ 2, New Cv.Scalar(255, 255, 0), 2)
                    Next

                    If isSmiling AndAlso smiles.Length > 0 Then
                        Dim s = smiles(0)
                        Dim sRect As New Cv.Rect(face.X + s.X, face.Y + lowerHalf.Y + s.Y, s.Width, s.Height)
                        Cv.Cv2.Rectangle(src, sRect, New Cv.Scalar(0, 255, 255), 2)
                    End If

                    Cv.Cv2.PutText(src, $"{category.ToUpperInvariant()} ({linkedinProb}%)", New Cv.Point(face.X, Math.Max(25, face.Y - 10)),
                        Cv.HersheyFonts.HersheySimplex, 0.7, color, 2)

                    Dim outBuf As Byte() = Nothing
                    Cv.Cv2.ImEncode(".jpg", src, outBuf, JpegQuality88)

                    Dim smileScore = If(isSmiling, Math.Clamp(65 + smiles.Length * 12, 60, 99), Random.Shared.Next(15, 35))
                    Return New ScanResult With {
                        .Success = True,
                        .Classification = category,
                        .Description = desc,
                        .LinkedInProbability = linkedinProb,
                        .AnnotatedImage = outBuf,
                        .Telemetry = New BiometricTelemetry With {
                            .SmileScore = smileScore,
                            .SymmetryScore = symmetry,
                            .BrightnessScore = Math.Clamp(CInt(brightness / 2.55), 10, 99),
                            .SharpnessScore = sharpness,
                            .EyesDetected = eyeCount,
                            .HasSmile = isSmiling
                        }
                    }
                End Using
            End Using
        End Using
    End Function

    Private Shared Function GetLargestRect(rects As Cv.Rect()) As Cv.Rect
        Dim best = rects(0)
        Dim bestArea = best.Width * best.Height
        For i = 1 To rects.Length - 1
            Dim area = rects(i).Width * rects(i).Height
            If area > bestArea Then
                best = rects(i)
                bestArea = area
            End If
        Next
        Return best
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _cascadeLock
            _faceCascadeAlt2?.Dispose()
            _faceCascadeDefault?.Dispose()
            _profileCascade?.Dispose()
            _eyeCascade?.Dispose()
            _smileCascade?.Dispose()
            _clahe.Dispose()
        End SyncLock
    End Sub
End Class
