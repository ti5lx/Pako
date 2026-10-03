Imports System.Globalization
Imports System.IO

' ======================================================================
' Configurar > Servicios de log: credenciales de QRZ, eQSL y LoTW,
' y la casilla para subir automaticamente a cada servicio.
' (La ventana se arma por codigo: en el disenador se ve vacia.)
' ======================================================================
Public Class FrmServicios
    Inherits Form

    ' QRZ
    Private chkQrz As New CheckBox()
    Private txtQrzKey As New TextBox()
    Private WithEvents btnQrzProbar As New Button()
    ' eQSL
    Private chkEqsl As New CheckBox()
    Private txtEqslUsuario As New TextBox()
    Private txtEqslClave As New TextBox()
    ' LoTW
    Private chkLotw As New CheckBox()
    Private txtTqsl As New TextBox()
    Private WithEvents btnTqsl As New Button()
    Private txtUbicacion As New TextBox()
    Private dtpDesde As New DateTimePicker()
    Private dtpHasta As New DateTimePicker()
    ' Botones
    Private WithEvents btnGuardar As New Button()
    Private btnCancelar As New Button()

    Private ReadOnly miIndicativo As String

    Public Sub New(indicativo As String)
        miIndicativo = indicativo
        Me.Text = Tr("Servicios de log")
        Me.Icon = Form1.IconoPako
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimizeBox = False
        Me.MaximizeBox = False
        Me.ClientSize = New Size(470, 330)

        Dim tabs As New TabControl() With {.Location = New Point(10, 10), .Size = New Size(450, 270)}
        Me.Controls.Add(tabs)

        ' ---------------- QRZ ----------------
        Dim tQrz As New TabPage("QRZ.com")
        tabs.TabPages.Add(tQrz)
        chkQrz.Text = Tr("Subir automaticamente cada QSO a {0}", "QRZ.com")
        chkQrz.AutoSize = True
        chkQrz.Location = New Point(15, 15)
        tQrz.Controls.Add(chkQrz)
        Etiqueta(tQrz, Tr("API key del logbook"), 15, 55)
        txtQrzKey.Location = New Point(140, 52)
        txtQrzKey.Width = 200
        txtQrzKey.UseSystemPasswordChar = True
        tQrz.Controls.Add(txtQrzKey)
        btnQrzProbar.Text = Tr("Probar")
        btnQrzProbar.Location = New Point(350, 50)
        btnQrzProbar.Width = 70
        tQrz.Controls.Add(btnQrzProbar)
        Nota(tQrz, 15, 95,
             Tr("NOTA_QRZ", miIndicativo))

        ' ---------------- eQSL ----------------
        Dim tEqsl As New TabPage("eQSL")
        tabs.TabPages.Add(tEqsl)
        chkEqsl.Text = Tr("Subir automaticamente cada QSO a {0}", "eQSL")
        chkEqsl.AutoSize = True
        chkEqsl.Location = New Point(15, 15)
        tEqsl.Controls.Add(chkEqsl)
        Etiqueta(tEqsl, Tr("Usuario"), 15, 55)
        txtEqslUsuario.Location = New Point(140, 52)
        txtEqslUsuario.Width = 200
        txtEqslUsuario.CharacterCasing = CharacterCasing.Upper
        tEqsl.Controls.Add(txtEqslUsuario)
        Etiqueta(tEqsl, Tr("Contrasena"), 15, 87)
        txtEqslClave.Location = New Point(140, 84)
        txtEqslClave.Width = 200
        txtEqslClave.UseSystemPasswordChar = True
        tEqsl.Controls.Add(txtEqslClave)
        Nota(tEqsl, 15, 125, Tr("El usuario de eQSL normalmente es tu indicativo."))

        ' ---------------- LoTW ----------------
        Dim tLotw As New TabPage("LoTW")
        tabs.TabPages.Add(tLotw)
        chkLotw.Text = Tr("Subir automaticamente cada QSO a {0}", "LoTW")
        chkLotw.AutoSize = True
        chkLotw.Location = New Point(15, 15)
        tLotw.Controls.Add(chkLotw)
        Etiqueta(tLotw, "tqsl.exe", 15, 55)
        txtTqsl.Location = New Point(140, 52)
        txtTqsl.Width = 200
        tLotw.Controls.Add(txtTqsl)
        btnTqsl.Text = Tr("Buscar...")
        btnTqsl.Location = New Point(350, 50)
        btnTqsl.Width = 70
        tLotw.Controls.Add(btnTqsl)
        Etiqueta(tLotw, "Station Location", 15, 87)
        txtUbicacion.Location = New Point(140, 84)
        txtUbicacion.Width = 200
        tLotw.Controls.Add(txtUbicacion)
        Etiqueta(tLotw, Tr("Desde (opcional)"), 15, 119)
        ConfigurarFecha(dtpDesde, New Point(140, 116))
        tLotw.Controls.Add(dtpDesde)
        Etiqueta(tLotw, Tr("Hasta (opcional)"), 15, 151)
        ConfigurarFecha(dtpHasta, New Point(140, 148))
        tLotw.Controls.Add(dtpHasta)
        Nota(tLotw, 15, 185,
             Tr("NOTA_LOTW"))

        ' ---------------- Botones ----------------
        btnGuardar.Text = Tr("Guardar")
        btnGuardar.Location = New Point(290, 292)
        btnGuardar.Size = New Size(80, 27)
        Me.Controls.Add(btnGuardar)
        btnCancelar.Text = Tr("Cancelar")
        btnCancelar.Location = New Point(380, 292)
        btnCancelar.Size = New Size(80, 27)
        btnCancelar.DialogResult = DialogResult.Cancel
        Me.Controls.Add(btnCancelar)
        Me.CancelButton = btnCancelar

        CargarValores()
    End Sub

    Private Shared Sub Etiqueta(padre As Control, texto As String, x As Integer, y As Integer)
        padre.Controls.Add(New Label() With {.Text = texto, .AutoSize = True, .Location = New Point(x, y)})
    End Sub

    Private Shared Sub Nota(padre As Control, x As Integer, y As Integer, texto As String)
        padre.Controls.Add(New Label() With {.Text = texto, .AutoSize = True, .ForeColor = Color.DimGray,
                                             .Location = New Point(x, y)})
    End Sub

    Private Shared Sub ConfigurarFecha(dtp As DateTimePicker, lugar As Point)
        dtp.Location = lugar
        dtp.Width = 130
        dtp.Format = DateTimePickerFormat.Custom
        dtp.CustomFormat = "yyyy-MM-dd"
        dtp.ShowCheckBox = True     ' sin marcar = sin limite
        dtp.Checked = False
    End Sub

    Private Shared Sub PonerFecha(dtp As DateTimePicker, texto As String)
        Dim f As DateTime
        If DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, f) Then
            dtp.Value = f
            dtp.Checked = True
        Else
            dtp.Checked = False
        End If
    End Sub

    Private Shared Function LeerFecha(dtp As DateTimePicker) As String
        Return If(dtp.Checked, dtp.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "")
    End Function

    Private Sub CargarValores()
        With My.Settings
            chkQrz.Checked = .QrzActivo
            txtQrzKey.Text = Credenciales.Descifrar(.QrzApiKey)
            chkEqsl.Checked = .EqslActivo
            txtEqslUsuario.Text = If(.EqslUsuario <> "", .EqslUsuario, miIndicativo)
            txtEqslClave.Text = Credenciales.Descifrar(.EqslClave)
            chkLotw.Checked = .LotwActivo
            txtTqsl.Text = .LotwTqsl
            If txtTqsl.Text = "" Then txtTqsl.Text = BuscarTqsl()
            txtUbicacion.Text = .LotwUbicacion
            PonerFecha(dtpDesde, .LotwDesde)
            PonerFecha(dtpHasta, .LotwHasta)
        End With
    End Sub

    ' Ubicaciones tipicas de TQSL
    Private Shared Function BuscarTqsl() As String
        For Each r In {"C:\Program Files (x86)\TrustedQSL\tqsl.exe", "C:\Program Files\TrustedQSL\tqsl.exe"}
            If File.Exists(r) Then Return r
        Next
        Return ""
    End Function

    Private Sub btnTqsl_Click(sender As Object, e As EventArgs) Handles btnTqsl.Click
        Using dlg As New OpenFileDialog()
            dlg.Title = Tr("Selecciona tqsl.exe")
            dlg.Filter = "TQSL (tqsl.exe)|tqsl.exe|" & Tr("Programas") & " (*.exe)|*.exe"
            If txtTqsl.Text <> "" AndAlso Directory.Exists(Path.GetDirectoryName(txtTqsl.Text)) Then
                dlg.InitialDirectory = Path.GetDirectoryName(txtTqsl.Text)
            End If
            If dlg.ShowDialog(Me) = DialogResult.OK Then txtTqsl.Text = dlg.FileName
        End Using
    End Sub

    Private Async Sub btnQrzProbar_Click(sender As Object, e As EventArgs) Handles btnQrzProbar.Click
        If txtQrzKey.Text.Trim() = "" Then
            MessageBox.Show(Me, Tr("Escribe la API key primero."))
            Return
        End If
        btnQrzProbar.Enabled = False
        Dim texto As String = Await ServiciosLog.QrzProbarAsync(txtQrzKey.Text)
        btnQrzProbar.Enabled = True
        MessageBox.Show(Me, texto, "QRZ.com")
    End Sub

    Private Sub btnGuardar_Click(sender As Object, e As EventArgs) Handles btnGuardar.Click
        ' Validar solo los servicios activados
        If chkQrz.Checked AndAlso txtQrzKey.Text.Trim() = "" Then
            MessageBox.Show(Me, Tr("Para subir a QRZ falta la API key.")) : Return
        End If
        If chkEqsl.Checked AndAlso (txtEqslUsuario.Text.Trim() = "" OrElse txtEqslClave.Text = "") Then
            MessageBox.Show(Me, Tr("Para subir a eQSL faltan el usuario o la contrasena.")) : Return
        End If
        If chkLotw.Checked Then
            If Not File.Exists(txtTqsl.Text.Trim()) OrElse
               Not Path.GetFileName(txtTqsl.Text.Trim()).Equals("tqsl.exe", StringComparison.OrdinalIgnoreCase) Then
                MessageBox.Show(Me, Tr("Para subir a LoTW selecciona el archivo tqsl.exe.")) : Return
            End If
            If txtUbicacion.Text.Trim() = "" Then
                MessageBox.Show(Me, Tr("Para subir a LoTW falta la Station Location (tal como se llama en TQSL).")) : Return
            End If
            If dtpDesde.Checked AndAlso dtpHasta.Checked AndAlso dtpDesde.Value.Date > dtpHasta.Value.Date Then
                MessageBox.Show(Me, Tr("La fecha 'Desde' es posterior a 'Hasta'.")) : Return
            End If
        End If

        With My.Settings
            .QrzActivo = chkQrz.Checked
            .QrzApiKey = Credenciales.Cifrar(txtQrzKey.Text.Trim())
            .EqslActivo = chkEqsl.Checked
            .EqslUsuario = txtEqslUsuario.Text.Trim()
            .EqslClave = Credenciales.Cifrar(txtEqslClave.Text)
            .LotwActivo = chkLotw.Checked
            .LotwTqsl = txtTqsl.Text.Trim()
            .LotwUbicacion = txtUbicacion.Text.Trim()
            .LotwDesde = LeerFecha(dtpDesde)
            .LotwHasta = LeerFecha(dtpHasta)
            .Save()
        End With
        Me.DialogResult = DialogResult.OK
    End Sub
End Class
