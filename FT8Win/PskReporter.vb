Imports System.IO
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions

' ======================================================================
' Reporte de estaciones escuchadas a PSKReporter (pskreporter.info)
' Protocolo IPFIX por UDP (report.pskreporter.info, puerto 4739),
' segun https://pskreporter.info/pskdev.html
' Se envia un paquete cada 5 minutos con las estaciones escuchadas.
' ======================================================================
Public Class PskReporter

    Private Const Servidor As String = "report.pskreporter.info"
    Private Const Puerto As Integer = 4739
    Private Const IntervaloSeg As Integer = 300       ' cada 5 minutos, como pide PSKReporter
    Private Const MaxBytesPaquete As Integer = 1400

    Private Class Spot
        Public Indicativo As String
        Public Grid As String
        Public FrecHz As Long
        Public Snr As Integer
        Public Modo As String
        Public Hora As DateTime
    End Class

    Private Shared ReadOnly Candado As New Object()
    Private Shared ReadOnly Pendientes As New Dictionary(Of String, Spot)
    Private Shared ReadOnly IdSesion As UInteger = CUInt(New Random().Next(1, Integer.MaxValue))
    Private Shared secuencia As UInteger = 0
    Private Shared ultimoEnvio As DateTime = DateTime.UtcNow
    Private Shared enviando As Boolean = False

    ' Datos de la estacion receptora (los pone Form1)
    Public Shared MiIndicativo As String = ""
    Public Shared MiGrid As String = ""
    Public Shared Software As String = "Pako"

    Private Shared ReadOnly FormatoIndicativo As New Regex("^(?=.*[0-9])(?=.*[A-Z])[A-Z0-9/]{3,13}$")

    ' Anotar una estacion escuchada. Una por indicativo y banda (queda la mas reciente).
    Public Shared Sub Agregar(indicativo As String, grid As String, frecHz As Long, snr As Integer, modo As String, hora As DateTime)
        If indicativo Is Nothing OrElse Not FormatoIndicativo.IsMatch(indicativo) Then Return
        If indicativo = MiIndicativo Then Return
        Dim clave As String = indicativo & "|" & (frecHz \ 1000000L).ToString()
        SyncLock Candado
            Dim anterior As Spot = Nothing
            If Pendientes.TryGetValue(clave, anterior) AndAlso String.IsNullOrEmpty(grid) Then grid = anterior.Grid
            Pendientes(clave) = New Spot With {.Indicativo = indicativo, .Grid = If(grid, ""), .FrecHz = frecHz,
                                               .Snr = snr, .Modo = modo, .Hora = hora}
        End SyncLock
    End Sub

    ' Llamar seguido (por ejemplo cada segundo): envia cuando pasaron 5 minutos
    Public Shared Sub Revisar()
        If enviando Then Return
        If (DateTime.UtcNow - ultimoEnvio).TotalSeconds < IntervaloSeg Then Return
        ultimoEnvio = DateTime.UtcNow
        Dim lote As List(Of Spot)
        SyncLock Candado
            If Pendientes.Count = 0 Then Return
            lote = Pendientes.Values.ToList()
            Pendientes.Clear()
        End SyncLock
        If MiIndicativo = "" OrElse MiGrid = "" Then Return
        enviando = True
        Task.Run(Sub() Enviar(lote))
    End Sub

    Public Shared ReadOnly Property CantidadPendiente As Integer
        Get
            SyncLock Candado
                Return Pendientes.Count
            End SyncLock
        End Get
    End Property

    Private Shared Sub Enviar(lote As List(Of Spot))
        Try
            Using udp As New UdpClient()
                Dim i As Integer = 0
                While i < lote.Count
                    ' Llenar un paquete hasta ~1400 bytes
                    Dim parte As New List(Of Spot)
                    Dim tam As Integer = 200
                    While i < lote.Count AndAlso tam < MaxBytesPaquete
                        parte.Add(lote(i))
                        tam += 20 + lote(i).Indicativo.Length + lote(i).Grid.Length + lote(i).Modo.Length
                        i += 1
                    End While
                    Dim paquete As Byte() = ArmarPaquete(parte)
                    udp.Send(paquete, paquete.Length, Servidor, Puerto)
                End While
            End Using
            Dim nombres = String.Join(", ", lote.Select(Function(s) s.Indicativo & If(s.Grid <> "", " (" & s.Grid & ")", "")))
            Trafico.Registrar("PSK", TipoTrafico.Envio,
                              Tr("Reportadas {0} estaciones como {1} ({2}):", lote.Count, MiIndicativo, MiGrid) & vbCrLf & nombres)
        Catch ex As Exception
            Trafico.Registrar("PSK", TipoTrafico.Error, Tr("No se pudo enviar a PSKReporter: {0}", ex.Message))
        Finally
            enviando = False
        End Try
    End Sub

    ' ------------------------------------------------------------------
    ' Armado del paquete IPFIX
    ' ------------------------------------------------------------------
    ' Plantilla del receptor: receiverCallsign, receiverLocator, decodingSoftware
    Private Shared ReadOnly PlantillaReceptor As Byte() = {
        &H0, &H3, &H0, &H24, &H99, &H92, &H0, &H3, &H0, &H0,
        &H80, &H2, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H80, &H4, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H80, &H8, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H0, &H0}

    ' Plantilla del emisor: senderCallsign, senderLocator, frequency, sNR, mode, informationSource, flowStartSeconds
    Private Shared ReadOnly PlantillaEmisor As Byte() = {
        &H0, &H2, &H0, &H3C, &H99, &H93, &H0, &H7,
        &H80, &H1, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H80, &H3, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H80, &H5, &H0, &H4, &H0, &H0, &H76, &H8F,
        &H80, &H6, &H0, &H1, &H0, &H0, &H76, &H8F,
        &H80, &HA, &HFF, &HFF, &H0, &H0, &H76, &H8F,
        &H80, &HB, &H0, &H1, &H0, &H0, &H76, &H8F,
        &H0, &H96, &H0, &H4}

    Private Shared Function ArmarPaquete(spots As List(Of Spot)) As Byte()
        ' Registro del receptor
        Dim receptor As New MemoryStream()
        Texto(receptor, MiIndicativo)
        Texto(receptor, MiGrid)
        Texto(receptor, Software)

        ' Registros de las estaciones escuchadas
        Dim emisores As New MemoryStream()
        For Each s In spots
            Texto(emisores, s.Indicativo)
            Texto(emisores, s.Grid)
            U32(emisores, CUInt(s.FrecHz))
            emisores.WriteByte(CByte(Math.Max(-128, Math.Min(127, s.Snr)) And &HFF))
            Texto(emisores, s.Modo)
            emisores.WriteByte(1)                             ' 1 = reporte automatico
            U32(emisores, CUInt((s.Hora - New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds))
        Next

        Dim cuerpo As New MemoryStream()
        cuerpo.Write(PlantillaReceptor, 0, PlantillaReceptor.Length)
        cuerpo.Write(PlantillaEmisor, 0, PlantillaEmisor.Length)
        Conjunto(cuerpo, &H9992, receptor.ToArray())
        Conjunto(cuerpo, &H9993, emisores.ToArray())

        ' Encabezado IPFIX
        Dim paquete As New MemoryStream()
        Dim total As Integer = 16 + CInt(cuerpo.Length)
        U16(paquete, CUShort(10))                                  ' version
        U16(paquete, CUShort(total))                               ' largo total
        U32(paquete, CUInt((DateTime.UtcNow - New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds))
        U32(paquete, secuencia)                                    ' numero de secuencia
        U32(paquete, IdSesion)                                     ' id aleatorio de esta sesion
        cuerpo.WriteTo(paquete)
        secuencia += CUInt(spots.Count + 1)
        Return paquete.ToArray()
    End Function

    ' Un conjunto de datos: id, largo y registros, rellenado a multiplo de 4 bytes
    Private Shared Sub Conjunto(destino As Stream, id As Integer, datos As Byte())
        Dim largo As Integer = 4 + datos.Length
        Dim relleno As Integer = (4 - largo Mod 4) Mod 4
        U16(destino, CUShort(id))
        U16(destino, CUShort(largo + relleno))
        destino.Write(datos, 0, datos.Length)
        For k As Integer = 1 To relleno
            destino.WriteByte(0)
        Next
    End Sub

    ' Texto de largo variable: 1 byte de largo + los caracteres ASCII
    Private Shared Sub Texto(destino As Stream, s As String)
        Dim b As Byte() = Encoding.ASCII.GetBytes(If(s, ""))
        Dim n As Integer = Math.Min(b.Length, 254)
        destino.WriteByte(CByte(n))
        destino.Write(b, 0, n)
    End Sub

    Private Shared Sub U16(destino As Stream, v As UShort)
        destino.WriteByte(CByte((v >> 8) And &HFF))
        destino.WriteByte(CByte(v And &HFF))
    End Sub

    Private Shared Sub U32(destino As Stream, v As UInteger)
        destino.WriteByte(CByte((v >> 24) And &HFF))
        destino.WriteByte(CByte((v >> 16) And &HFF))
        destino.WriteByte(CByte((v >> 8) And &HFF))
        destino.WriteByte(CByte(v And &HFF))
    End Sub
End Class
