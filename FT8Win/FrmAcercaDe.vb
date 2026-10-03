' ======================================================================
' Menu "Acerca de": autor, version y licencia (GPL v3) de Pako.
' (La ventana se arma por codigo: en el disenador se ve vacia.)
' ======================================================================
Public Class FrmAcercaDe
    Inherits Form

    Public Const AvisoWsjtx As String =
            "The algorithms, source code, look-and-feel of WSJT-X, MAP65, QMAP, and related programs, and " &
            "protocol specifications for the modes FSK441, FST4, FST4W, FT4, FT8, ISCAT, JT4, JT6M, JT9, " &
            "JT65, JTMS, JTTY, MSK144, QRA64, Q65, and WSPR are Copyright (C) 2001-2026 by one or more of " &
            "the following authors: Joseph Taylor, K1JT; Bill Somerville, G4WJS; Steven Franke, K9AN; Nico " &
            "Palermo, IV3NWV; Greg Beam, KI7MT; Michael Black, W9MDB; Edson Pereira, PY2SDR; Philip Karn, " &
            "KA9Q; Uwe Risse, DG2YCB; Brian Moran, N9ADG; Roger Rehr, W3SZ; John Nelson, G4KLA; Charlie " &
            "Suckling, DL3WDG; Terrell Deppe, KJ5HST; David Christle, KD0BTO; and other members of the WSJT " &
            "Development Team."

    Public Sub New()
        Me.Text = Tr("Acerca de") & " " & Form1.NombrePrograma
        Me.Icon = Form1.IconoPako
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimizeBox = False
        Me.MaximizeBox = False
        Me.ShowInTaskbar = False
        Me.BackColor = Color.White
        Me.ClientSize = New Size(480, 570)

        ' Icono grande
        Dim pic As New PictureBox() With {
            .Location = New Point(20, 20),
            .Size = New Size(64, 64),
            .SizeMode = PictureBoxSizeMode.Zoom}
        Try
            pic.Image = New Icon(Form1.IconoPako, 64, 64).ToBitmap()
        Catch
        End Try
        Me.Controls.Add(pic)

        ' Nombre y version
        Me.Controls.Add(New Label() With {
            .Text = Form1.NombrePrograma & "  V." & Form1.VersionPrograma,
            .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(0, 90, 60),
            .AutoSize = True,
            .Location = New Point(100, 18)})
        Me.Controls.Add(New Label() With {
            .Text = Tr("El comunicador Bribri"),
            .Font = New Font("Segoe UI", 11.0F, FontStyle.Italic),
            .ForeColor = Color.DimGray,
            .AutoSize = True,
            .Location = New Point(104, 60)})

        ' Texto
        Dim texto As String =
            Tr("ACERCA_TEXTO", Form1.Autor)
        Me.Controls.Add(New Label() With {
            .Text = texto,
            .AutoSize = True,
            .Location = New Point(22, 105)})

        ' Aviso de copyright que WSJT-X pide mostrar en los programas que usan su codigo (JTTY).
        ' Va en ingles, tal como lo publica WSJT-X.
        Me.Controls.Add(New Label() With {
            .Text = AvisoWsjtx,
            .Font = New Font("Segoe UI", 7.5F),
            .ForeColor = Color.DimGray,
            .AutoSize = False,
            .Size = New Size(440, 110),
            .Location = New Point(22, 412)})

        ' Boton cerrar
        Dim btnCerrar As New Button() With {
            .Text = Tr("Cerrar"),
            .Size = New Size(90, 28),
            .Location = New Point(370, 532),
            .DialogResult = DialogResult.OK}
        Me.Controls.Add(btnCerrar)
        Me.AcceptButton = btnCerrar
        Me.CancelButton = btnCerrar
    End Sub
End Class
