Imports System.IO
Imports System.IO.Ports

' ======================================================================
' Configurar > Radio (CAT): modelo de radio (lista de Hamlib), puerto,
' velocidad y split. (La ventana se arma por codigo.)
' ======================================================================
Public Class FrmCat
    Inherits Form

    Private txtHamlib As New TextBox()
    Private WithEvents btnHamlib As New Button()
    Private WithEvents cmbRadio As New ComboBox()
    Private cmbPuerto As New ComboBox()
    Private cmbBaudios As New ComboBox()
    Private chkSplit As New CheckBox()
    Private WithEvents btnProbar As New Button()
    Private WithEvents btnGuardar As New Button()
    Private btnCancelar As New Button()
    Private lblNota As New Label()
    Private lblCarpeta As New Label()
    Private lblHamlibAuto As New Label()

    Private ReadOnly Ninguno As New RadioModelo With {.Numero = 0, .Marca = "(" & Tr("Ninguno") & ")", .Modelo = Tr("sin CAT")}

    Public Sub New()
        Me.Text = Tr("Radio (CAT)")
        Me.Icon = Form1.IconoPako
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimizeBox = False
        Me.MaximizeBox = False
        Me.ClientSize = New Size(500, 330)

        lblCarpeta.Text = Tr("Carpeta de Hamlib")
        lblCarpeta.AutoSize = True
        lblCarpeta.Location = New Point(15, 20)
        Me.Controls.Add(lblCarpeta)
        lblHamlibAuto.AutoSize = True
        lblHamlibAuto.ForeColor = Color.DimGray
        lblHamlibAuto.Location = New Point(15, 20)
        lblHamlibAuto.Visible = False
        Me.Controls.Add(lblHamlibAuto)
        txtHamlib.Location = New Point(140, 17)
        txtHamlib.Width = 260
        Me.Controls.Add(txtHamlib)
        btnHamlib.Text = Tr("Buscar...")
        btnHamlib.Location = New Point(410, 15)
        btnHamlib.Width = 75
        Me.Controls.Add(btnHamlib)

        Etiqueta(Tr("Radio"), 15, 57)
        cmbRadio.Location = New Point(140, 54)
        cmbRadio.Width = 345
        cmbRadio.DropDownStyle = ComboBoxStyle.DropDown
        cmbRadio.AutoCompleteMode = AutoCompleteMode.SuggestAppend
        cmbRadio.AutoCompleteSource = AutoCompleteSource.ListItems
        cmbRadio.MaxDropDownItems = 25
        Me.Controls.Add(cmbRadio)

        Etiqueta(Tr("Puerto CAT"), 15, 94)
        cmbPuerto.Location = New Point(140, 91)
        cmbPuerto.Width = 100
        cmbPuerto.DropDownStyle = ComboBoxStyle.DropDownList
        cmbPuerto.Items.AddRange(SerialPort.GetPortNames().OrderBy(Function(p) p.Length).ThenBy(Function(p) p).ToArray())
        Me.Controls.Add(cmbPuerto)

        Etiqueta(Tr("Velocidad"), 260, 94)
        cmbBaudios.Location = New Point(330, 91)
        cmbBaudios.Width = 90
        cmbBaudios.DropDownStyle = ComboBoxStyle.DropDownList
        cmbBaudios.Items.AddRange(New Object() {"1200", "4800", "9600", "19200", "38400", "57600", "115200"})
        Me.Controls.Add(cmbBaudios)

        chkSplit.Text = Tr("Split (Fake It): mover el VFO para que el audio TX quede entre 1500 y 2000 Hz")
        chkSplit.AutoSize = True
        chkSplit.Location = New Point(15, 130)
        Me.Controls.Add(chkSplit)

        lblNota.AutoSize = False
        lblNota.Location = New Point(15, 160)
        lblNota.Size = New Size(470, 110)
        lblNota.ForeColor = Color.DimGray
        lblNota.Text =
            Tr("NOTA_CAT")
        Me.Controls.Add(lblNota)

        btnProbar.Text = Tr("Probar")
        btnProbar.Location = New Point(15, 290)
        btnProbar.Size = New Size(80, 27)
        Me.Controls.Add(btnProbar)
        btnGuardar.Text = Tr("Guardar")
        btnGuardar.Location = New Point(315, 290)
        btnGuardar.Size = New Size(80, 27)
        Me.Controls.Add(btnGuardar)
        btnCancelar.Text = Tr("Cancelar")
        btnCancelar.Location = New Point(405, 290)
        btnCancelar.Size = New Size(80, 27)
        btnCancelar.DialogResult = DialogResult.Cancel
        Me.Controls.Add(btnCancelar)
        Me.CancelButton = btnCancelar

        ' Valores guardados
        txtHamlib.Text = Cat.BuscarHamlib(My.Settings.CatHamlib)
        If txtHamlib.Text = "" Then txtHamlib.Text = My.Settings.CatHamlib
        If cmbPuerto.Items.Contains(My.Settings.CatPuerto) Then cmbPuerto.SelectedItem = My.Settings.CatPuerto
        Dim b As String = If(My.Settings.CatBaudios > 0, My.Settings.CatBaudios.ToString(), "9600")
        cmbBaudios.SelectedItem = If(cmbBaudios.Items.Contains(b), b, "9600")
        chkSplit.Checked = My.Settings.CatSplit

        ' Si Hamlib viene con Pako (o esta el de WSJT-X), no hace falta elegir carpeta
        Dim automatica As String = Cat.BuscarHamlib("")
        If automatica <> "" Then
            txtHamlib.Text = automatica
            lblCarpeta.Visible = False
            txtHamlib.Visible = False
            btnHamlib.Visible = False
            lblHamlibAuto.Visible = True
            lblHamlibAuto.Text = If(automatica.ToLower().Contains("wsjtx"),
                                    Tr("Control de radios: se usa el Hamlib de WSJT-X instalado en esta PC."),
                                    Tr("Control de radios: Hamlib incluido con Pako."))
        End If
        LlenarRadios()
    End Sub

    Private Sub Etiqueta(texto As String, x As Integer, y As Integer)
        Me.Controls.Add(New Label() With {.Text = texto, .AutoSize = True, .Location = New Point(x, y)})
    End Sub

    Private Sub LlenarRadios()
        cmbRadio.BeginUpdate()
        cmbRadio.Items.Clear()
        cmbRadio.Items.Add(Ninguno)
        Dim carpeta As String = Cat.BuscarHamlib(txtHamlib.Text.Trim())
        If carpeta <> "" Then
            txtHamlib.Text = carpeta
            For Each r In Cat.ListarRadios(carpeta)
                cmbRadio.Items.Add(r)
            Next
        End If
        cmbRadio.EndUpdate()
        cmbRadio.SelectedIndex = 0
        For i As Integer = 0 To cmbRadio.Items.Count - 1
            If DirectCast(cmbRadio.Items(i), RadioModelo).Numero = My.Settings.CatModelo Then
                cmbRadio.SelectedIndex = i
                Exit For
            End If
        Next
        If carpeta = "" Then
            lblNota.ForeColor = Color.Firebrick
            lblNota.Text = Tr("NO_HAMLIB")
        End If
    End Sub

    Private Sub btnHamlib_Click(sender As Object, e As EventArgs) Handles btnHamlib.Click
        Using dlg As New FolderBrowserDialog()
            dlg.Description = Tr("Carpeta de Hamlib (la que tiene rigctld.exe, normalmente 'bin')")
            If Directory.Exists(txtHamlib.Text) Then dlg.SelectedPath = txtHamlib.Text
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtHamlib.Text = dlg.SelectedPath
                LlenarRadios()
            End If
        End Using
    End Sub

    Private Function RadioElegido() As RadioModelo
        Dim r = TryCast(cmbRadio.SelectedItem, RadioModelo)
        If r IsNot Nothing Then Return r
        ' Escrito a mano: buscar por texto
        For Each item In cmbRadio.Items
            If item.ToString().Equals(cmbRadio.Text, StringComparison.OrdinalIgnoreCase) Then Return DirectCast(item, RadioModelo)
        Next
        Return Nothing
    End Function

    Private Async Sub btnProbar_Click(sender As Object, e As EventArgs) Handles btnProbar.Click
        Dim r = RadioElegido()
        If r Is Nothing OrElse r.Numero = 0 Then
            MessageBox.Show(Me, Tr("Elige un radio de la lista."), Form1.NombrePrograma) : Return
        End If
        If cmbPuerto.SelectedItem Is Nothing Then
            MessageBox.Show(Me, Tr("Elige el puerto CAT."), Form1.NombrePrograma) : Return
        End If
        Dim carpeta As String = Cat.BuscarHamlib(txtHamlib.Text.Trim())
        Dim puerto As String = cmbPuerto.SelectedItem.ToString()
        Dim baudios As Integer = CInt(cmbBaudios.SelectedItem)
        btnProbar.Enabled = False
        Me.Cursor = Cursors.WaitCursor
        Dim ok As Boolean = Await Task.Run(Function() Cat.Conectar(carpeta, r.Numero, puerto, baudios))
        Dim hz As Long = Cat.FrecuenciaHz
        Cat.Desconectar()
        Me.Cursor = Cursors.Default
        btnProbar.Enabled = True
        If ok Then
            MessageBox.Show(Me, Tr("El radio responde. Frecuencia: {0} MHz", (hz / 1000000.0).ToString("0.000000")), "CAT")
        Else
            MessageBox.Show(Me, Tr("No funciono: {0}", Cat.UltimoError), "CAT")
        End If
    End Sub

    Private Sub btnGuardar_Click(sender As Object, e As EventArgs) Handles btnGuardar.Click
        Dim r = RadioElegido()
        If r Is Nothing Then
            MessageBox.Show(Me, Tr("Elige un radio de la lista (o '(Ninguno)')."), Form1.NombrePrograma) : Return
        End If
        If r.Numero <> 0 AndAlso cmbPuerto.SelectedItem Is Nothing Then
            MessageBox.Show(Me, Tr("Elige el puerto CAT."), Form1.NombrePrograma) : Return
        End If
        With My.Settings
            .CatModelo = r.Numero
            .CatPuerto = If(cmbPuerto.SelectedItem Is Nothing, "", cmbPuerto.SelectedItem.ToString())
            .CatBaudios = CInt(cmbBaudios.SelectedItem)
            .CatSplit = chkSplit.Checked
            .CatHamlib = txtHamlib.Text.Trim()
            .Save()
        End With
        Me.DialogResult = DialogResult.OK
    End Sub
End Class
