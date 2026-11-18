Imports System.Diagnostics
Imports System.IO
Imports System.Media
Imports System.Speech.Recognition
Imports Cv = OpenCvSharp

Public Class Form1
    Inherits Form

    Private Const MatchRounds As Integer = 10
    Private Const RoundTimeMs As Integer = 10000
    Private Const RequiredHoldMs As Integer = 380

    Private _repo As GameRepository
    Private _cv As OpenCvService

    ' Estado del juego
    Private _mode As String = "free"
    Private _soundEnabled As Boolean = True
    Private _currentProfile As ProfileEntry
    Private _canGuess As Boolean = False
    Private _answered As Boolean = False
    Private _points As Integer = 0
    Private _score As Integer = 0
    Private _total As Integer = 0
    Private _streak As Integer = 0
    Private _maxStreak As Integer = 0
    Private ReadOnly _history As New List(Of Boolean)()

    ' Temporizador de partida
    Private ReadOnly _roundTimer As New Timer() With {.Interval = 40}
    Private ReadOnly _autoNextTimer As New Timer() With {.Interval = 2000}
    Private _roundSw As Stopwatch

    ' Cámara y control facial
    Private _capture As Cv.VideoCapture
    Private ReadOnly _camTimer As New Timer() With {.Interval = 33}
    Private ReadOnly _latestFrame As New Cv.Mat()
    Private ReadOnly _frameLock As New Object()
    Private _camReady As Boolean = False
    Private _faceActive As Boolean = False
    Private _detectingFace As Boolean = False
    Private _faceDetected As Boolean = False
    Private _faceAction As String = "Center"
    Private _lastAction As String = "Center"
    Private _profileAngle As Boolean = False
    Private _normX As Double, _normY As Double, _normW As Double, _normH As Double
    Private _holdSw As Stopwatch
    Private _lastLatencyMs As Long = 0
    Private _fpsCounter As Integer = 0
    Private _currentFps As Integer = 0
    Private ReadOnly _fpsSw As Stopwatch = Stopwatch.StartNew()

    ' Comandos de voz
    Private _speech As SpeechRecognitionEngine
    Private _voiceActive As Boolean = False

    ' Controles UI
    Private _btnModeFree As Button
    Private _btnModeMatch As Button
    Private _btnSound As Button
    Private _lblPoints As Label
    Private _lblScore As Label
    Private _lblAcc As Label
    Private _lblStreak As Label
    Private _lblProfiles As Label

    Private _lblRound As Label
    Private _timerBack As Panel
    Private _timerFill As Panel
    Private _chkGray As CheckBox
    Private _chkBlur As CheckBox
    Private _portraitBorder As Panel
    Private _picPortrait As PictureBox
    Private _lblReveal As Label
    Private _btnLi As Button
    Private _btnIn As Button
    Private _lblFeedback As Label
    Private _btnNext As Button
    Private _lblHistory As Label

    Private _picCam As PictureBox
    Private _lblCamStatus As Label
    Private _holdBar As ProgressBar
    Private _btnFace As Button
    Private _btnVoice As Button
    Private _lblVoiceStatus As Label
    Private _btnScan As Button
    Private _picScan As PictureBox
    Private _lblScanInfo As Label

    ' Paleta oscura
    Private ReadOnly _bgDark As Color = Color.FromArgb(11, 16, 32)
    Private ReadOnly _surface As Color = Color.FromArgb(19, 27, 48)
    Private ReadOnly _surface2 As Color = Color.FromArgb(27, 37, 64)
    Private ReadOnly _border As Color = Color.FromArgb(40, 52, 90)
    Private ReadOnly _textMain As Color = Color.FromArgb(241, 245, 249)
    Private ReadOnly _textMuted As Color = Color.FromArgb(163, 177, 201)
    Private ReadOnly _blueLi As Color = Color.FromArgb(10, 102, 194)
    Private ReadOnly _redIn As Color = Color.FromArgb(200, 30, 30)
    Private ReadOnly _greenOk As Color = Color.FromArgb(52, 211, 153)
    Private ReadOnly _accent As Color = Color.FromArgb(139, 92, 246)

    Public Sub New()
        InitializeComponent()
        Text = "LinkedIn o Interpol — 100% Visual Basic Edition"
        Size = New Size(1060, 760)
        MinimumSize = New Size(980, 720)
        StartPosition = FormStartPosition.CenterScreen
        BackColor = _bgDark
        ForeColor = _textMain
        Font = New Font("Segoe UI", 9.5F)
        KeyPreview = True
        DoubleBuffered = True

        BuildUI()

        AddHandler Load, AddressOf OnFormLoad
        AddHandler FormClosing, AddressOf HandleFormClosing
        AddHandler _roundTimer.Tick, AddressOf OnRoundTimerTick
        AddHandler _autoNextTimer.Tick, AddressOf OnAutoNextTick
        AddHandler _camTimer.Tick, AddressOf OnCamTick
    End Sub

    Private Sub BuildUI()
        Dim header As New Panel() With {.Dock = DockStyle.Top, .Height = 104, .Padding = New Padding(18, 10, 18, 6)}

        Dim lblTitle As New Label() With {
            .Text = "LinkedIn o Interpol (Visual Basic)",
            .Font = New Font("Segoe UI", 16.0F, FontStyle.Bold),
            .Location = New Point(18, 10),
            .AutoSize = True
        }

        _btnModeFree = MakeButton("Modo Libre", _accent, New Point(600, 10), New Size(110, 32))
        _btnModeMatch = MakeButton("Partida de 10", _surface2, New Point(716, 10), New Size(125, 32))
        _btnSound = MakeButton("Sonido: ON", _surface2, New Point(848, 10), New Size(95, 32))
        Dim btnRanking = MakeButton("Ranking", _surface2, New Point(950, 10), New Size(78, 32))

        AddHandler _btnModeFree.Click, Sub() StartGame("free")
        AddHandler _btnModeMatch.Click, Sub() StartGame("match")
        AddHandler _btnSound.Click, Sub()
                                        _soundEnabled = Not _soundEnabled
                                        _btnSound.Text = If(_soundEnabled, "Sonido: ON", "Sonido: OFF")
                                    End Sub
        AddHandler btnRanking.Click, Sub() ShowSummary()

        header.Controls.AddRange({lblTitle, _btnModeFree, _btnModeMatch, _btnSound, btnRanking})

        Dim statsX = 18
        _lblPoints = AddStatBox(header, "PUNTOS", "0", statsX, 50) : statsX += 196
        _lblScore = AddStatBox(header, "ACIERTOS", "0", statsX, 50) : statsX += 196
        _lblAcc = AddStatBox(header, "PRECISIÓN", "-", statsX, 50) : statsX += 196
        _lblStreak = AddStatBox(header, "RACHA", "0", statsX, 50) : statsX += 196
        _lblProfiles = AddStatBox(header, "PERFILES", "...", statsX, 50)

        Dim body As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .RowCount = 1,
            .Padding = New Padding(14, 4, 14, 14)
        }
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))

        ' --- COLUMNA IZQUIERDA: JUEGO ---
        Dim leftCard As New Panel() With {.Dock = DockStyle.Fill, .BackColor = _surface, .Margin = New Padding(6), .Padding = New Padding(16)}

        Dim gameTitle As New Label() With {
            .Text = "¿Profesional de LinkedIn o buscado por Interpol?",
            .Font = New Font("Segoe UI", 11.0F, FontStyle.Bold),
            .Location = New Point(16, 12),
            .Size = New Size(350, 24)
        }
        _lblRound = New Label() With {
            .Text = "Ronda 1",
            .ForeColor = _textMuted,
            .TextAlign = ContentAlignment.MiddleRight,
            .Location = New Point(370, 12),
            .Size = New Size(110, 24)
        }

        _timerBack = New Panel() With {.Location = New Point(16, 40), .Size = New Size(464, 8), .BackColor = _surface2, .Visible = False}
        _timerFill = New Panel() With {.Location = New Point(0, 0), .Size = New Size(464, 8), .BackColor = _greenOk}
        _timerBack.Controls.Add(_timerFill)

        _chkGray = New CheckBox() With {.Text = "Blanco y negro", .Location = New Point(120, 54), .AutoSize = True, .ForeColor = _textMuted}
        _chkBlur = New CheckBox() With {.Text = "Desenfoque", .Location = New Point(255, 54), .AutoSize = True, .ForeColor = _textMuted}
        AddHandler _chkGray.CheckedChanged, Async Sub() Await LoadPortraitAsync()
        AddHandler _chkBlur.CheckedChanged, Async Sub() Await LoadPortraitAsync()

        _portraitBorder = New Panel() With {.Location = New Point(78, 84), .Size = New Size(340, 340), .BackColor = _border, .Padding = New Padding(3)}
        _picPortrait = New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = _surface2}
        _lblReveal = New Label() With {
            .Dock = DockStyle.Bottom,
            .Height = 68,
            .BackColor = Color.FromArgb(215, 8, 12, 24),
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 8.8F, FontStyle.Bold),
            .Padding = New Padding(8, 4, 8, 4),
            .Visible = False
        }
        _picPortrait.Controls.Add(_lblReveal)
        _portraitBorder.Controls.Add(_picPortrait)

        _btnLi = MakeButton("← LinkedIn", _blueLi, New Point(78, 436), New Size(165, 46))
        _btnLi.Font = New Font("Segoe UI", 10.5F, FontStyle.Bold)
        _btnIn = MakeButton("Interpol →", _redIn, New Point(253, 436), New Size(165, 46))
        _btnIn.Font = New Font("Segoe UI", 10.5F, FontStyle.Bold)
        AddHandler _btnLi.Click, Sub() SubmitGuess("LinkedIn")
        AddHandler _btnIn.Click, Sub() SubmitGuess("Interpol")

        _lblFeedback = New Label() With {
            .Location = New Point(16, 490),
            .Size = New Size(464, 30),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold)
        }

        _btnNext = MakeButton("Siguiente ronda →", _surface2, New Point(78, 524), New Size(340, 40))
        _btnNext.Visible = False
        AddHandler _btnNext.Click, Async Sub() Await NextRoundAsync()

        _lblHistory = New Label() With {
            .Location = New Point(16, 570),
            .Size = New Size(464, 24),
            .TextAlign = ContentAlignment.MiddleCenter,
            .ForeColor = _textMuted,
            .Font = New Font("Consolas", 10.0F, FontStyle.Bold)
        }

        leftCard.Controls.AddRange({gameTitle, _lblRound, _timerBack, _chkGray, _chkBlur, _portraitBorder, _btnLi, _btnIn, _lblFeedback, _btnNext, _lblHistory})

        ' --- COLUMNA DERECHA: CÁMARA, CONTROL FACIAL, VOZ Y ESCÁNER ---
        Dim rightCard As New Panel() With {.Dock = DockStyle.Fill, .BackColor = _surface, .Margin = New Padding(6), .Padding = New Padding(16)}

        Dim camTitle As New Label() With {
            .Text = "Cámara, control facial, voz y escáner",
            .Font = New Font("Segoe UI", 11.0F, FontStyle.Bold),
            .Location = New Point(16, 12),
            .Size = New Size(464, 24)
        }

        _picCam = New PictureBox() With {
            .Location = New Point(38, 42),
            .Size = New Size(420, 290),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.FromArgb(5, 8, 17),
            .BorderStyle = BorderStyle.FixedSingle
        }
        _lblCamStatus = New Label() With {
            .Text = "Iniciando cámara y OpenCV...",
            .ForeColor = _textMuted,
            .BackColor = Color.FromArgb(200, 5, 8, 17),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _picCam.Controls.Add(_lblCamStatus)

        _holdBar = New ProgressBar() With {.Location = New Point(38, 334), .Size = New Size(420, 8), .Maximum = 100}

        _btnFace = MakeButton("Activar control facial", Color.FromArgb(4, 120, 87), New Point(38, 350), New Size(205, 40))
        _btnVoice = MakeButton("Activar comandos de voz", _surface2, New Point(253, 350), New Size(205, 40))
        _btnScan = MakeButton("Escanear mi cara y añadirme", Color.FromArgb(109, 40, 217), New Point(38, 396), New Size(420, 40))

        AddHandler _btnFace.Click, AddressOf OnToggleFace
        AddHandler _btnVoice.Click, AddressOf OnToggleVoice
        AddHandler _btnScan.Click, AddressOf OnBiometricScan

        _lblVoiceStatus = New Label() With {
            .Location = New Point(38, 440),
            .Size = New Size(420, 22),
            .ForeColor = _greenOk,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Text = "Atajos: Flechas ← / →, Enter, voz («LinkedIn»/«Interpol») o cámara"
        }

        _picScan = New PictureBox() With {
            .Location = New Point(38, 468),
            .Size = New Size(130, 120),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = _bgDark,
            .BorderStyle = BorderStyle.FixedSingle
        }
        _lblScanInfo = New Label() With {
            .Location = New Point(178, 468),
            .Size = New Size(280, 120),
            .BackColor = _bgDark,
            .ForeColor = _textMain,
            .Padding = New Padding(8),
            .Text = "Escáner biométrico listo." & Environment.NewLine & "Pulsa «Escanear mi cara» para analizarte y añadirte a las rondas."
        }

        rightCard.Controls.AddRange({camTitle, _picCam, _holdBar, _btnFace, _btnVoice, _btnScan, _lblVoiceStatus, _picScan, _lblScanInfo})

        body.Controls.Add(leftCard, 0, 0)
        body.Controls.Add(rightCard, 1, 0)

        Controls.Add(body)
        Controls.Add(header)
    End Sub

    Private Function AddStatBox(parent As Control, title As String, initial As String, x As Integer, y As Integer) As Label
        Dim box As New Panel() With {.Location = New Point(x, y), .Size = New Size(188, 46), .BackColor = _surface}
        Dim lblT As New Label() With {.Text = title, .Font = New Font("Segoe UI", 7.5F, FontStyle.Bold), .ForeColor = _textMuted, .Location = New Point(8, 4), .AutoSize = True}
        Dim lblV As New Label() With {.Text = initial, .Font = New Font("Segoe UI", 13.0F, FontStyle.Bold), .ForeColor = Color.White, .Location = New Point(8, 20), .AutoSize = True}
        box.Controls.AddRange({lblT, lblV})
        parent.Controls.Add(box)
        Return lblV
    End Function

    Private Function MakeButton(text As String, bg As Color, loc As Point, sz As Size) As Button
        Dim b As New Button() With {
            .Text = text,
            .BackColor = bg,
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat,
            .Location = loc,
            .Size = sz,
            .Cursor = Cursors.Hand,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        }
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    ' ================= INICIO NATIVO EN VISUAL BASIC =================
    Private Async Sub OnFormLoad(sender As Object, e As EventArgs)
        SetChoicesEnabled(False)
        _lblFeedback.Text = "Cargando clasificadores OpenCV y perfiles..."

        _repo = New GameRepository()
        _cv = New OpenCvService(_repo.DataRoot)
        Await _cv.EnsureCascadesAsync()

        _lblProfiles.Text = _repo.Count.ToString()
        InitCamera()
        Await NextRoundAsync()
    End Sub

    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        _roundTimer.Stop()
        _autoNextTimer.Stop()
        _camTimer.Stop()
        Try : _speech?.RecognizeAsyncStop() : _speech?.Dispose() : Catch : End Try
        Try : _capture?.Release() : _capture?.Dispose() : Catch : End Try
        Try : _cv?.Dispose() : Catch : End Try
    End Sub

    ' ================= LÓGICA DEL JUEGO =================
    Private Sub StartGame(mode As String)
        _autoNextTimer.Stop()
        _roundTimer.Stop()
        _mode = mode
        _btnModeFree.BackColor = If(mode = "free", _accent, _surface2)
        _btnModeMatch.BackColor = If(mode = "match", _accent, _surface2)

        _points = 0 : _score = 0 : _total = 0 : _streak = 0 : _maxStreak = 0
        _history.Clear()
        UpdateStats()
        Dim t = NextRoundAsync()
    End Sub

    Private Sub UpdateStats()
        _lblPoints.Text = _points.ToString()
        _lblScore.Text = _score.ToString()
        _lblAcc.Text = If(_total > 0, $"{Math.Round(_score * 100.0 / _total)}%", "-")
        _lblStreak.Text = _streak.ToString()
        _lblHistory.Text = String.Join("  ", _history.Skip(Math.Max(0, _history.Count - 10)).Select(Function(ok) If(ok, "[OK]", "[X]")))
    End Sub

    Private Sub SetChoicesEnabled(enabled As Boolean)
        _btnLi.Enabled = enabled
        _btnIn.Enabled = enabled
    End Sub

    Private Async Function NextRoundAsync() As Task
        _autoNextTimer.Stop()
        _roundTimer.Stop()

        If _mode = "match" AndAlso _total >= MatchRounds Then
            ShowSummary()
            Return
        End If

        _canGuess = False
        _answered = False
        SetChoicesEnabled(False)
        _btnNext.Visible = False
        _lblFeedback.Text = ""
        _lblReveal.Visible = False
        _portraitBorder.BackColor = _border
        _timerBack.Visible = (_mode = "match")
        _timerFill.Width = _timerBack.Width
        _lblRound.Text = If(_mode = "match", $"Ronda {Math.Min(_total + 1, MatchRounds)}/{MatchRounds}", $"Ronda {_total + 1}")

        Dim excludeId As Guid? = If(_currentProfile IsNot Nothing, _currentProfile.Id, CType(Nothing, Guid?))
        _currentProfile = _repo.GetRandomProfile(excludeId)
        _lblProfiles.Text = _repo.Count.ToString()
        Await LoadPortraitAsync()
    End Function

    Private Async Function LoadPortraitAsync() As Task
        If _currentProfile Is Nothing OrElse _cv Is Nothing Then Return
        Try
            Dim path = _currentProfile.ImagePath
            Dim gray = _chkGray.Checked
            Dim blur = _chkBlur.Checked
            Dim bytes = Await Task.Run(Function() _cv.ProcessGamePortrait(path, gray, blur))

            Using ms As New MemoryStream(bytes)
                Using img = Image.FromStream(ms)
                    Dim old = _picPortrait.Image
                    _picPortrait.Image = New Bitmap(img)
                    old?.Dispose()
                End Using
            End Using

            If Not _answered Then
                _canGuess = True
                SetChoicesEnabled(True)
                If _mode = "match" Then
                    _roundSw = Stopwatch.StartNew()
                    _roundTimer.Start()
                End If
            End If
        Catch
            _lblFeedback.Text = "Error cargando retrato."
        End Try
    End Function

    Private Sub SubmitGuess(guess As String)
        If Not _canGuess OrElse _currentProfile Is Nothing Then Return
        _canGuess = False
        _answered = True
        _roundTimer.Stop()
        SetChoicesEnabled(False)

        Dim elapsedMs = If(_roundSw IsNot Nothing, _roundSw.ElapsedMilliseconds, 0)
        Dim isCorrect = String.Equals(_currentProfile.Category, guess, StringComparison.OrdinalIgnoreCase)

        _total += 1
        If isCorrect Then
            _score += 1
            _streak += 1
            _maxStreak = Math.Max(_maxStreak, _streak)
            Dim mult = 1.0 + Math.Min(_streak - 1, 4) * 0.25
            Dim bonus = If(_mode = "match", CInt(Math.Max(0, RoundTimeMs - elapsedMs) / 100), 0)
            _points += CInt(Math.Round((100 + bonus) * mult))
            If _soundEnabled Then SystemSounds.Asterisk.Play()
        Else
            _streak = 0
            If _soundEnabled Then SystemSounds.Hand.Play()
        End If

        _history.Add(isCorrect)
        UpdateStats()

        _portraitBorder.BackColor = If(isCorrect, _greenOk, _redIn)
        _lblReveal.Text = $"{_currentProfile.Category.ToUpperInvariant()}: {_currentProfile.Description}"
        _lblReveal.Visible = True

        _lblFeedback.ForeColor = If(isCorrect, _greenOk, _redIn)
        _lblFeedback.Text = If(guess = "Timeout", "¡Se acabó el tiempo!", If(isCorrect, $"¡Acertaste! (Racha x{_streak})", "Fallaste."))

        Dim over = (_mode = "match" AndAlso _total >= MatchRounds)
        _btnNext.Text = If(over, "Ver resultados", "Siguiente ronda →")
        _btnNext.Visible = True

        If _faceActive OrElse _voiceActive OrElse over Then
            _autoNextTimer.Interval = If(over, 2600, 2000)
            _autoNextTimer.Start()
        End If
    End Sub

    Private Sub OnRoundTimerTick(sender As Object, e As EventArgs)
        If _roundSw Is Nothing OrElse Not _canGuess Then Return
        Dim left = RoundTimeMs - _roundSw.ElapsedMilliseconds
        If left <= 0 Then
            _timerFill.Width = 0
            SubmitGuess("Timeout")
            Return
        End If
        _timerFill.Width = CInt(_timerBack.Width * (left / CDbl(RoundTimeMs)))
    End Sub

    Private Async Sub OnAutoNextTick(sender As Object, e As EventArgs)
        _autoNextTimer.Stop()
        Await NextRoundAsync()
    End Sub

    Private Sub ShowSummary()
        If _repo Is Nothing Then Return
        Dim acc = If(_total > 0, CInt(Math.Round(_score * 100.0 / _total)), 0)
        Using dlg As New SummaryForm(_repo, _points, acc, _maxStreak)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrEmpty(dlg.PlayAgainMode) Then
                StartGame(dlg.PlayAgainMode)
            End If
        End Using
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Left AndAlso _canGuess Then
            SubmitGuess("LinkedIn")
            Return True
        ElseIf keyData = Keys.Right AndAlso _canGuess Then
            SubmitGuess("Interpol")
            Return True
        ElseIf keyData = Keys.Enter AndAlso _btnNext.Visible Then
            Dim t = NextRoundAsync()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ' ================= CÁMARA & CONTROL FACIAL DIRECTO =================
    Private Sub InitCamera()
        Try
            _capture = New Cv.VideoCapture(0)
            If _capture.IsOpened() Then
                _capture.FrameWidth = 640
                _capture.FrameHeight = 480
                _camReady = True
                _lblCamStatus.Visible = False
                _camTimer.Start()
            Else
                _lblCamStatus.Text = "Cámara no disponible. Juega con botones, teclado o voz."
                _btnFace.Enabled = False
                _btnScan.Enabled = False
            End If
        Catch
            _lblCamStatus.Text = "No se pudo abrir la cámara."
            _btnFace.Enabled = False
            _btnScan.Enabled = False
        End Try
    End Sub

    Private Sub OnCamTick(sender As Object, e As EventArgs)
        If Not _camReady OrElse _capture Is Nothing Then Return
        Using raw As New Cv.Mat()
            If Not _capture.Read(raw) OrElse raw.Empty() Then Return
            Cv.Cv2.Flip(raw, raw, Cv.FlipMode.Y)

            SyncLock _frameLock
                raw.CopyTo(_latestFrame)
            End SyncLock

            _fpsCounter += 1
            If _fpsSw.ElapsedMilliseconds >= 1000 Then
                _currentFps = CInt(Math.Round(_fpsCounter * 1000.0 / _fpsSw.ElapsedMilliseconds))
                _fpsCounter = 0
                _fpsSw.Restart()
            End If

            If _faceActive Then
                DrawFaceOverlay(raw)
                UpdateFaceHold()
                If Not _detectingFace Then
                    Dim t = DetectFaceAsync(raw.Clone())
                End If
            End If

            Cv.Cv2.PutText(raw, $"Cam: {_currentFps} FPS | {_lastLatencyMs} ms", New Cv.Point(10, 24), Cv.HersheyFonts.HersheySimplex, 0.55, Cv.Scalar.LightGreen, 2)

            Dim old = _picCam.Image
            _picCam.Image = MatToBitmap(raw)
            old?.Dispose()
        End Using
    End Sub

    Private Sub DrawFaceOverlay(frame As Cv.Mat)
        Dim w = frame.Width
        Dim h = frame.Height
        Dim leftX = CInt(w * 0.38)
        Dim rightX = CInt(w * 0.62)

        Cv.Cv2.Line(frame, New Cv.Point(leftX, 0), New Cv.Point(leftX, h), New Cv.Scalar(234, 142, 59), 2)
        Cv.Cv2.Line(frame, New Cv.Point(rightX, 0), New Cv.Point(rightX, h), New Cv.Scalar(82, 82, 240), 2)
        Cv.Cv2.PutText(frame, "<- LINKEDIN", New Cv.Point(12, h - 14), Cv.HersheyFonts.HersheySimplex, 0.6, New Cv.Scalar(255, 190, 120), 2)
        Cv.Cv2.PutText(frame, "INTERPOL ->", New Cv.Point(w - 145, h - 14), Cv.HersheyFonts.HersheySimplex, 0.6, New Cv.Scalar(120, 120, 255), 2)

        If _faceDetected Then
            Dim bx = CInt(_normX * w), by = CInt(_normY * h), bw = CInt(_normW * w), bh = CInt(_normH * h)
            Dim col = If(_faceAction = "LinkedIn", New Cv.Scalar(234, 142, 59), If(_faceAction = "Interpol", New Cv.Scalar(82, 82, 240), New Cv.Scalar(153, 211, 52)))
            Cv.Cv2.Rectangle(frame, New Cv.Rect(bx, by, bw, bh), col, 3)
            Dim txt = If(_profileAngle, $"{_faceAction.ToUpperInvariant()} (PERFIL)", _faceAction.ToUpperInvariant())
            Cv.Cv2.PutText(frame, txt, New Cv.Point(bx, Math.Max(20, by - 8)), Cv.HersheyFonts.HersheySimplex, 0.65, col, 2)
        End If
    End Sub

    Private Sub UpdateFaceHold()
        If _canGuess AndAlso (_faceAction = "LinkedIn" OrElse _faceAction = "Interpol") AndAlso _holdSw IsNot Nothing Then
            Dim pct = CInt(Math.Min(100, (_holdSw.ElapsedMilliseconds * 100.0) / RequiredHoldMs))
            _holdBar.Value = pct
            If pct >= 100 Then
                Dim chosen = _faceAction
                _faceAction = "Center" : _lastAction = "Center" : _holdSw = Nothing
                _holdBar.Value = 0
                SubmitGuess(chosen)
            End If
        Else
            _holdBar.Value = 0
        End If
    End Sub

    Private Async Function DetectFaceAsync(frame As Cv.Mat) As Task
        _detectingFace = True
        Dim sw = Stopwatch.StartNew()
        Try
            Dim res = Await Task.Run(Function()
                                         Using frame
                                             Return _cv.DetectFaceControl(frame)
                                         End Using
                                     End Function)
            _lastLatencyMs = sw.ElapsedMilliseconds
            _faceDetected = res.FaceDetected
            _profileAngle = res.IsProfile
            If res.FaceDetected Then
                _normX = res.NormX : _normY = res.NormY : _normW = res.NormW : _normH = res.NormH
            End If
            If res.Action = "LinkedIn" OrElse res.Action = "Interpol" Then
                If res.Action <> _lastAction Then
                    _lastAction = res.Action
                    _holdSw = Stopwatch.StartNew()
                End If
            Else
                _lastAction = "Center"
                _holdSw = Nothing
            End If
            _faceAction = res.Action
        Catch
        Finally
            _detectingFace = False
        End Try
    End Function

    Private Sub OnToggleFace(sender As Object, e As EventArgs)
        _faceActive = Not _faceActive
        _btnFace.Text = If(_faceActive, "Desactivar control facial", "Activar control facial")
        _btnFace.BackColor = If(_faceActive, Color.FromArgb(180, 83, 9), Color.FromArgb(4, 120, 87))
        If Not _faceActive Then
            _faceDetected = False : _faceAction = "Center" : _lastAction = "Center" : _holdBar.Value = 0
        End If
    End Sub

    ' ================= COMANDOS DE VOZ (System.Speech) =================
    Private Sub OnToggleVoice(sender As Object, e As EventArgs)
        If _voiceActive Then
            _voiceActive = False
            _btnVoice.Text = "Activar comandos de voz"
            _btnVoice.BackColor = _surface2
            _lblVoiceStatus.Text = "Comandos de voz desactivados."
            Try : _speech?.RecognizeAsyncCancel() : Catch : End Try
            Return
        End If

        Try
            If _speech Is Nothing Then
                Dim info = SpeechRecognitionEngine.InstalledRecognizers().
                    FirstOrDefault(Function(r) r.Culture.TwoLetterISOLanguageName = "es")
                _speech = If(info IsNot Nothing, New SpeechRecognitionEngine(info), New SpeechRecognitionEngine())

                Dim choices As New Choices("linkedin", "ejecutivo", "izquierda", "interpol", "criminal", "derecha", "siguiente", "otra")
                Dim gb As New GrammarBuilder(choices)
                If info IsNot Nothing Then gb.Culture = info.Culture
                _speech.LoadGrammar(New Grammar(gb))
                _speech.SetInputToDefaultAudioDevice()
                AddHandler _speech.SpeechRecognized, AddressOf OnSpeechRecognized
            End If

            _speech.RecognizeAsync(RecognizeMode.Multiple)
            _voiceActive = True
            _btnVoice.Text = "Desactivar comandos de voz"
            _btnVoice.BackColor = Color.FromArgb(15, 118, 110)
            _lblVoiceStatus.Text = "Escuchando... Di «LinkedIn» o «Interpol»"
        Catch
            _voiceActive = False
            _lblVoiceStatus.Text = "Voz no disponible en este equipo (sin micrófono o paquete de idioma)."
        End Try
    End Sub

    Private Sub OnSpeechRecognized(sender As Object, e As SpeechRecognizedEventArgs)
        If e.Result.Confidence < 0.45F Then Return
        Dim word = e.Result.Text.ToLowerInvariant()
        BeginInvoke(Sub()
                        _lblVoiceStatus.Text = $"Voz: «{word}»"
                        If (word = "linkedin" OrElse word = "ejecutivo" OrElse word = "izquierda") AndAlso _canGuess Then
                            SubmitGuess("LinkedIn")
                        ElseIf (word = "interpol" OrElse word = "criminal" OrElse word = "derecha") AndAlso _canGuess Then
                            SubmitGuess("Interpol")
                        ElseIf (word = "siguiente" OrElse word = "otra") AndAlso _btnNext.Visible Then
                            Dim t = NextRoundAsync()
                        End If
                    End Sub)
    End Sub

    ' ================= ESCÁNER BIOMÉTRICO =================
    Private Async Sub OnBiometricScan(sender As Object, e As EventArgs)
        If Not _camReady OrElse _cv Is Nothing OrElse _repo Is Nothing Then Return
        _btnScan.Enabled = False
        _lblScanInfo.Text = "Analizando ojos, sonrisa, simetría y luz..."

        Try
            Dim snapshot As Cv.Mat
            SyncLock _frameLock
                If _latestFrame.Empty() Then Return
                snapshot = _latestFrame.Clone()
            End SyncLock

            Dim res = Await Task.Run(Function() _cv.AnalyzeAndCatalogFace(snapshot))
            If Not res.Success Then
                snapshot.Dispose()
                _lblScanInfo.Text = res.Description
                Return
            End If

            Dim tag = res.Classification.ToLowerInvariant()
            Dim fileName = Path.Combine(_repo.AssetsDir, $"user_{tag}_{Guid.NewGuid():N}.jpg")
            Cv.Cv2.ImWrite(fileName, snapshot)
            snapshot.Dispose()
            _repo.AddProfile(fileName, res.Classification, res.Description)
            _lblProfiles.Text = _repo.Count.ToString()

            If res.AnnotatedImage IsNot Nothing AndAlso res.AnnotatedImage.Length > 0 Then
                Using ms As New MemoryStream(res.AnnotatedImage)
                    Using img = Image.FromStream(ms)
                        Dim old = _picScan.Image
                        _picScan.Image = New Bitmap(img)
                        old?.Dispose()
                    End Using
                End Using
            End If

            Dim t = res.Telemetry
            _lblScanInfo.Text =
                $"{res.Classification.ToUpperInvariant()} ({res.LinkedInProbability}%)" & Environment.NewLine &
                res.Description & Environment.NewLine &
                $"Sonrisa: {t?.SmileScore}% | Simetría: {t?.SymmetryScore}% | Luz: {t?.BrightnessScore}% | Nitidez: {t?.SharpnessScore}%"
        Catch
            _lblScanInfo.Text = "Error al realizar el escaneo biométrico."
        Finally
            _btnScan.Enabled = True
        End Try
    End Sub

    Private Shared Function MatToBitmap(mat As Cv.Mat) As Bitmap
        Dim bytes As Byte() = Nothing
        Cv.Cv2.ImEncode(".bmp", mat, bytes)
        Using ms As New MemoryStream(bytes)
            Return New Bitmap(ms)
        End Using
    End Function
End Class
