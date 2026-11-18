Public Class SummaryForm
    Inherits Form

    Private ReadOnly _repo As GameRepository
    Private ReadOnly _points As Integer
    Private ReadOnly _accuracy As Integer
    Private ReadOnly _maxStreak As Integer

    Private ReadOnly _txtName As New TextBox()
    Private ReadOnly _btnSave As New Button()
    Private ReadOnly _lblStatus As New Label()
    Private ReadOnly _grid As New DataGridView()

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property PlayAgainMode As String = ""

    Public Sub New(repo As GameRepository, points As Integer, accuracy As Integer, maxStreak As Integer)
        _repo = repo
        _points = points
        _accuracy = accuracy
        _maxStreak = maxStreak

        Text = "Fin de la partida - LinkedIn o Interpol"
        Size = New Size(520, 560)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        BackColor = Color.FromArgb(19, 27, 48)
        ForeColor = Color.FromArgb(241, 245, 249)
        Font = New Font("Segoe UI", 9.5F)

        BuildLayout()
        RenderGrid(_repo.GetLeaderboard())
    End Sub

    Private Sub BuildLayout()
        Dim lblTitle As New Label() With {
            .Text = GetRank(_accuracy),
            .Font = New Font("Segoe UI", 13.0F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(251, 191, 36),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Dock = DockStyle.Top,
            .Height = 42
        }

        Dim lblStats As New Label() With {
            .Text = $"Puntos: {_points}    |    Precisión: {_accuracy}%    |    Mejor racha: {_maxStreak}",
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Dock = DockStyle.Top,
            .Height = 32
        }

        Dim savePanel As New Panel() With {.Dock = DockStyle.Top, .Height = 76, .Padding = New Padding(16, 8, 16, 4)}
        _txtName.PlaceholderText = "Tu nombre para el ranking..."
        _txtName.Location = New Point(16, 10)
        _txtName.Size = New Size(310, 28)
        _txtName.BackColor = Color.FromArgb(27, 37, 64)
        _txtName.ForeColor = Color.White
        _txtName.BorderStyle = BorderStyle.FixedSingle

        _btnSave.Text = "Guardar"
        _btnSave.Location = New Point(336, 9)
        _btnSave.Size = New Size(150, 30)
        _btnSave.FlatStyle = FlatStyle.Flat
        _btnSave.FlatAppearance.BorderSize = 0
        _btnSave.BackColor = Color.FromArgb(109, 40, 217)
        _btnSave.ForeColor = Color.White
        _btnSave.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        AddHandler _btnSave.Click, AddressOf OnSaveScore

        _lblStatus.Location = New Point(16, 44)
        _lblStatus.Size = New Size(470, 22)
        _lblStatus.ForeColor = Color.FromArgb(163, 177, 201)

        savePanel.Controls.AddRange({_txtName, _btnSave, _lblStatus})

        _grid.Dock = DockStyle.Fill
        _grid.BackgroundColor = Color.FromArgb(11, 16, 32)
        _grid.BorderStyle = BorderStyle.None
        _grid.AllowUserToAddRows = False
        _grid.AllowUserToDeleteRows = False
        _grid.ReadOnly = True
        _grid.RowHeadersVisible = False
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        _grid.EnableHeadersVisualStyles = False
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(27, 37, 64)
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        _grid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(19, 27, 48)
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(241, 245, 249)
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 52, 90)
        _grid.GridColor = Color.FromArgb(40, 52, 90)

        _grid.Columns.Add("Pos", "#")
        _grid.Columns.Add("Player", "Jugador")
        _grid.Columns.Add("Points", "Puntos")
        _grid.Columns.Add("Acc", "Precisión")
        _grid.Columns(0).FillWeight = 20
        _grid.Columns(1).FillWeight = 50
        _grid.Columns(2).FillWeight = 25
        _grid.Columns(3).FillWeight = 25

        Dim btnPanel As New Panel() With {.Dock = DockStyle.Bottom, .Height = 56, .Padding = New Padding(16, 10, 16, 10)}
        Dim btnMatch As New Button() With {
            .Text = "Jugar otra partida (10)",
            .Location = New Point(16, 10),
            .Size = New Size(225, 36),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(10, 102, 194),
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        }
        btnMatch.FlatAppearance.BorderSize = 0
        AddHandler btnMatch.Click, Sub()
                                       PlayAgainMode = "match"
                                       DialogResult = DialogResult.OK
                                       Close()
                                   End Sub

        Dim btnFree As New Button() With {
            .Text = "Modo libre",
            .Location = New Point(256, 10),
            .Size = New Size(228, 36),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(40, 52, 90),
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        }
        btnFree.FlatAppearance.BorderSize = 0
        AddHandler btnFree.Click, Sub()
                                      PlayAgainMode = "free"
                                      DialogResult = DialogResult.OK
                                      Close()
                                  End Sub

        btnPanel.Controls.AddRange({btnMatch, btnFree})

        Controls.Add(_grid)
        Controls.Add(savePanel)
        Controls.Add(lblStats)
        Controls.Add(lblTitle)
        Controls.Add(btnPanel)
    End Sub

    Private Shared Function GetRank(acc As Integer) As String
        If acc >= 90 Then Return "Agente especial de Interpol"
        If acc >= 70 Then Return "Headhunter senior"
        If acc >= 50 Then Return "Becario con intuición"
        Return "Te han hackeado el LinkedIn"
    End Function

    Private Sub RenderGrid(items As List(Of ScoreEntry))
        _grid.Rows.Clear()
        If items Is Nothing Then Return
        For i = 0 To items.Count - 1
            Dim s = items(i)
            _grid.Rows.Add(i + 1, s.PlayerName, s.Points, $"{s.Accuracy}%")
        Next
    End Sub

    Private Sub OnSaveScore(sender As Object, e As EventArgs)
        Dim name = _txtName.Text.Trim()
        If String.IsNullOrEmpty(name) Then
            _lblStatus.Text = "Escribe un nombre primero."
            _txtName.Focus()
            Return
        End If

        Dim top10 = _repo.AddScore(New ScoreRequest With {
            .PlayerName = name,
            .Points = _points,
            .Accuracy = _accuracy,
            .MaxStreak = _maxStreak
        })
        RenderGrid(top10)
        _btnSave.Enabled = False
        _txtName.Enabled = False
        _lblStatus.ForeColor = Color.FromArgb(52, 211, 153)
        _lblStatus.Text = "Puntuación guardada correctamente."
    End Sub
End Class
