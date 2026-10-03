Imports System.IO

' ======================================================================
' Log > Ver trafico con los servidores
' Muestra en vivo lo que Pako envia a QRZ / eQSL / LoTW y lo que responden.
' (La ventana se arma por codigo: en el disenador se ve vacia.)
' ======================================================================
Public Class FrmTrafico
    Inherits Form

    Private txt As New RichTextBox()
    Private WithEvents cmbFiltro As New ComboBox()
    Private WithEvents btnLimpiar As New Button()
    Private WithEvents btnCopiar As New Button()
    Private WithEvents btnArchivo As New Button()
    Private lblResumen As New Label()

    Private ReadOnly manejador As Trafico.NuevaEventHandler

    Public Sub New()
        Me.Text = Tr("Trafico con los servidores")
        Me.Icon = Form1.IconoPako
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.Size = New Size(820, 600)
        Me.MinimumSize = New Size(500, 300)
        Me.StartPosition = FormStartPosition.CenterParent

        ' --- Barra de arriba ---
        Dim barra As New Panel() With {.Dock = DockStyle.Top, .Height = 38}
        barra.Controls.Add(New Label() With {.Text = Tr("Mostrar:"), .AutoSize = True, .Location = New Point(10, 11)})
        cmbFiltro.DropDownStyle = ComboBoxStyle.DropDownList
        cmbFiltro.Items.AddRange(New Object() {Tr("Todos"), "QRZ", "eQSL", "LoTW", "PSK", "CAT"})
        cmbFiltro.SelectedIndex = 0
        cmbFiltro.Location = New Point(65, 8)
        cmbFiltro.Width = 90
        barra.Controls.Add(cmbFiltro)

        btnLimpiar.Text = Tr("Limpiar")
        btnLimpiar.Location = New Point(170, 7)
        btnLimpiar.Width = 75
        barra.Controls.Add(btnLimpiar)

        btnCopiar.Text = Tr("Copiar todo")
        btnCopiar.Location = New Point(250, 7)
        btnCopiar.Width = 85
        barra.Controls.Add(btnCopiar)

        btnArchivo.Text = Tr("Abrir archivo del historial")
        btnArchivo.Location = New Point(340, 7)
        btnArchivo.Width = 160
        barra.Controls.Add(btnArchivo)

        lblResumen.AutoSize = True
        lblResumen.ForeColor = Color.DimGray
        lblResumen.Location = New Point(515, 11)
        barra.Controls.Add(lblResumen)

        ' --- Texto ---
        txt.Dock = DockStyle.Fill
        txt.ReadOnly = True
        txt.BackColor = Color.FromArgb(250, 250, 250)
        txt.Font = New Font("Consolas", 9.5F)
        txt.WordWrap = False
        txt.DetectUrls = False

        Me.Controls.Add(txt)
        Me.Controls.Add(barra)
        txt.BringToFront()

        ' Escuchar el trafico nuevo
        manejador = AddressOf AlLlegar
        AddHandler Trafico.Nueva, manejador
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        MostrarTodo()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler Trafico.Nueva, manejador
        MyBase.OnFormClosed(e)
    End Sub

    ' Puede llegar desde otro hilo: pasar al hilo de la ventana
    Private Sub AlLlegar(entrada As EntradaTrafico)
        If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() Agregar(entrada, True))
        Else
            Agregar(entrada, True)
        End If
    End Sub

    Private Sub MostrarTodo()
        txt.Clear()
        For Each e In Trafico.Historial()
            Agregar(e, False)
        Next
        txt.SelectionStart = txt.TextLength
        txt.ScrollToCaret()
        ActualizarResumen()
    End Sub

    Private Sub Agregar(e As EntradaTrafico, alFinal As Boolean)
        Dim filtro As String = cmbFiltro.SelectedItem.ToString()
        If cmbFiltro.SelectedIndex > 0 AndAlso filtro <> e.Servicio Then Return

        Dim colorTitulo As Color
        Select Case e.Tipo
            Case TipoTrafico.Envio : colorTitulo = Color.RoyalBlue
            Case TipoTrafico.Respuesta : colorTitulo = Color.ForestGreen
            Case TipoTrafico.Error : colorTitulo = Color.Firebrick
            Case Else : colorTitulo = Color.DimGray
        End Select

        ' Encabezado en negrita y color
        txt.SelectionStart = txt.TextLength
        txt.SelectionLength = 0
        txt.SelectionColor = colorTitulo
        txt.SelectionFont = New Font(txt.Font, FontStyle.Bold)
        txt.AppendText(String.Format("[{0:HH:mm:ss} UTC]  {1,-5} {2}", e.Hora, e.Servicio, Trafico.NombreTipo(e.Tipo)) & vbCrLf)

        ' Contenido, con sangria
        txt.SelectionStart = txt.TextLength
        txt.SelectionColor = If(e.Tipo = TipoTrafico.Error, Color.Firebrick, Color.Black)
        txt.SelectionFont = txt.Font
        For Each linea In e.Texto.Replace(vbCrLf, vbLf).Split(ChrW(10))
            txt.AppendText("    " & linea & vbCrLf)
        Next
        txt.AppendText(vbCrLf)

        If alFinal Then
            txt.SelectionStart = txt.TextLength
            txt.ScrollToCaret()
            ActualizarResumen()
        End If
    End Sub

    Private Sub ActualizarResumen()
        Dim h = Trafico.Historial()
        Dim errores As Integer = h.Where(Function(x) x.Tipo = TipoTrafico.Error).Count()
        lblResumen.Text = Tr("{0} mensajes", h.Count) & If(errores > 0, "   (" & Tr("{0} con error", errores) & ")", "")
        lblResumen.ForeColor = If(errores > 0, Color.Firebrick, Color.DimGray)
    End Sub

    Private Sub cmbFiltro_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbFiltro.SelectedIndexChanged
        If Me.IsHandleCreated Then MostrarTodo()
    End Sub

    Private Sub btnLimpiar_Click(sender As Object, e As EventArgs) Handles btnLimpiar.Click
        Trafico.Limpiar()     ' solo la ventana; el archivo del historial se conserva
        MostrarTodo()
    End Sub

    Private Sub btnCopiar_Click(sender As Object, e As EventArgs) Handles btnCopiar.Click
        If txt.TextLength > 0 Then Clipboard.SetText(txt.Text)
    End Sub

    Private Sub btnArchivo_Click(sender As Object, e As EventArgs) Handles btnArchivo.Click
        If File.Exists(Trafico.RutaArchivo) Then
            Process.Start("notepad.exe", Chr(34) & Trafico.RutaArchivo & Chr(34))
        Else
            MessageBox.Show(Me, Tr("Todavia no hay trafico registrado."))
        End If
    End Sub
End Class
