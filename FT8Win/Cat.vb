Imports System.IO
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

' ======================================================================
' Control del radio por CAT usando Hamlib (el mismo que usa WSJT-X).
' Pako arranca rigctld.exe (de Hamlib) en segundo plano y le habla por
' TCP en 127.0.0.1:4532 con comandos de texto:
'   f            -> leer frecuencia (Hz)
'   F 14074000   -> poner frecuencia
'   M USB 0      -> poner modo
' Hamlib es LGPL y se usa como programa aparte.
' ======================================================================
Public Class RadioModelo
    Public Numero As Integer
    Public Marca As String
    Public Modelo As String
    Public Overrides Function ToString() As String
        Return Marca & "  " & Modelo & "   (" & Numero & ")"
    End Function
End Class

Public Class Cat
    Private Const PuertoTcp As Integer = 4532

    Private Shared proceso As Process
    Private Shared cliente As TcpClient
    Private Shared lector As StreamReader
    Private Shared escritor As StreamWriter
    Private Shared ReadOnly Candado As New Object()
    Private Shared sondeo As Threading.Timer
    Private Shared errores As Integer = 0

    Public Shared Property Conectado As Boolean = False
    Public Shared Property FrecuenciaHz As Long = 0       ' ultima frecuencia leida del radio
    Public Shared Property UltimoError As String = ""

    ' Avisa cuando cambia la frecuencia del radio (desde otro hilo)
    Public Shared Event FrecuenciaCambio(hz As Long)
    ' Avisa cuando se pierde la conexion
    Public Shared Event Desconectado(motivo As String)

    ' ------------------------------------------------------------------
    ' Donde esta Hamlib
    ' ------------------------------------------------------------------
    Public Shared Function BuscarHamlib(Optional preferida As String = "") As String
        Dim lugares As New List(Of String)
        If preferida <> "" Then
            lugares.Add(preferida)
            lugares.Add(Path.Combine(preferida, "bin"))
        End If
        lugares.Add(Path.Combine(Application.StartupPath, "hamlib"))
        lugares.Add(Path.Combine(Application.StartupPath, "hamlib", "bin"))
        lugares.Add(Application.StartupPath)
        ' Si WSJT-X esta instalado, trae su propio Hamlib (rigctld-wsjtx.exe): usarlo tambien
        For Each pf In {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "C:\"}
            If pf = "" Then Continue For
            lugares.Add(Path.Combine(pf, "wsjtx", "bin"))
            lugares.Add(Path.Combine(pf, "WSJT", "wsjtx", "bin"))
        Next
        lugares.Add("C:\WSJT\wsjtx\bin")
        For Each l In lugares
            If ExeDemonio(l) <> "" Then Return l
        Next
        ' Al descomprimir el zip de Hamlib queda una subcarpeta (hamlib-w64-4.x.x\bin): buscar adentro
        If preferida <> "" AndAlso Directory.Exists(preferida) Then
            Try
                Dim encontrado = Directory.EnumerateFiles(preferida, "rigctld*.exe", SearchOption.AllDirectories).FirstOrDefault()
                If encontrado IsNot Nothing Then Return Path.GetDirectoryName(encontrado)
            Catch
            End Try
        End If
        Return ""
    End Function

    ' rigctld.exe (Hamlib) o rigctld-wsjtx.exe (el que instala WSJT-X)
    Public Shared Function ExeDemonio(carpeta As String) As String
        For Each n In {"rigctld.exe", "rigctld-wsjtx.exe"}
            Dim r As String = Path.Combine(carpeta, n)
            If File.Exists(r) Then Return r
        Next
        Return ""
    End Function

    Private Shared Function ExeLista(carpeta As String) As String
        For Each n In {"rigctl.exe", "rigctl-wsjtx.exe"}
            Dim r As String = Path.Combine(carpeta, n)
            If File.Exists(r) Then Return r
        Next
        Return ""
    End Function

    ' ------------------------------------------------------------------
    ' Lista de radios: la que da "rigctl -l" (la misma de WSJT-X)
    ' ------------------------------------------------------------------
    Private Shared listaCache As List(Of RadioModelo)

    Public Shared Function ListarRadios(carpeta As String) As List(Of RadioModelo)
        If listaCache IsNot Nothing AndAlso listaCache.Count > 0 Then Return listaCache
        Dim lista As New List(Of RadioModelo)
        Dim exe As String = ExeLista(carpeta)
        If exe = "" Then Return lista
        Dim salida As String = ""
        Try
            Dim psi As New ProcessStartInfo(exe, "-l") With {
                .UseShellExecute = False, .CreateNoWindow = True,
                .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = carpeta}
            Using p As Process = Process.Start(psi)
                salida = p.StandardOutput.ReadToEnd()
                p.WaitForExit(10000)
            End Using
        Catch
            Return lista
        End Try

        ' " 3013  Icom                   IC-718                  20210425.0      Stable      RIG_MODEL_IC718"
        Dim formato As New Regex("^\s*(\d+)\s+(.+?)\s{2,}(.+?)\s{2,}\S+\s+\S+")
        For Each linea In salida.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim m = formato.Match(linea)
            If Not m.Success Then Continue For
            lista.Add(New RadioModelo With {.Numero = CInt(m.Groups(1).Value),
                                            .Marca = m.Groups(2).Value.Trim(),
                                            .Modelo = m.Groups(3).Value.Trim()})
        Next
        lista.Sort(Function(a, b)
                       Dim c = String.Compare(a.Marca, b.Marca, StringComparison.OrdinalIgnoreCase)
                       Return If(c <> 0, c, String.Compare(a.Modelo, b.Modelo, StringComparison.OrdinalIgnoreCase))
                   End Function)
        listaCache = lista
        Return lista
    End Function

    ' ------------------------------------------------------------------
    ' Conectar / desconectar
    ' ------------------------------------------------------------------
    Public Shared Function Conectar(carpeta As String, modelo As Integer, puerto As String, baudios As Integer) As Boolean
        Desconectar()
        UltimoError = ""
        Dim exe As String = ExeDemonio(carpeta)
        If exe = "" Then
            UltimoError = Tr("No se encontro rigctld.exe (Hamlib) en {0}", carpeta)
            Return False
        End If

        Try
            Dim args As String = String.Format("-m {0} -T 127.0.0.1 -t {1}", modelo, PuertoTcp)
            If puerto <> "" Then args &= " -r " & puerto
            If baudios > 0 Then args &= " -s " & baudios
            Dim psi As New ProcessStartInfo(exe, args) With {
                .UseShellExecute = False, .CreateNoWindow = True,
                .RedirectStandardError = True, .RedirectStandardOutput = True,
                .WorkingDirectory = carpeta}
            proceso = Process.Start(psi)
            Trafico.Registrar("CAT", TipoTrafico.Envio, "rigctld " & args)

            ' Esperar a que rigctld abra el puerto TCP (hasta 5 s)
            Dim limite As DateTime = DateTime.UtcNow.AddSeconds(5)
            While DateTime.UtcNow < limite
                If proceso.HasExited Then
                    UltimoError = Tr("rigctld se cerro: {0}", proceso.StandardError.ReadToEnd().Trim())
                    Exit While
                End If
                Try
                    cliente = New TcpClient()
                    cliente.Connect("127.0.0.1", PuertoTcp)
                    Exit While
                Catch
                    cliente = Nothing
                    Thread.Sleep(200)
                End Try
            End While
            If cliente Is Nothing Then
                If UltimoError = "" Then UltimoError = Tr("No se pudo conectar con rigctld.")
                Desconectar()
                Return False
            End If

            cliente.ReceiveTimeout = 3000
            cliente.SendTimeout = 3000
            Dim red = cliente.GetStream()
            lector = New StreamReader(red, Encoding.ASCII)
            escritor = New StreamWriter(red, Encoding.ASCII) With {.AutoFlush = True, .NewLine = vbLf}

            ' Primera lectura: confirma que el radio responde
            Dim hz As Long = LeerFrecuencia()
            If hz <= 0 Then
                If UltimoError = "" Then UltimoError = Tr("El radio no responde por CAT (revisa puerto, velocidad y la direccion CI-V).")
                Desconectar()
                Return False
            End If
            Conectado = True
            errores = 0
            Trafico.Registrar("CAT", TipoTrafico.Respuesta, Tr("Conectado. Frecuencia del radio: {0} Hz", hz))
            sondeo = New Threading.Timer(AddressOf Sondear, Nothing, 1000, 1000)
            Return True
        Catch ex As Exception
            UltimoError = ex.Message
            Desconectar()
            Return False
        End Try
    End Function

    Public Shared Sub Desconectar()
        Conectado = False
        Try
            If sondeo IsNot Nothing Then sondeo.Dispose()
        Catch
        End Try
        sondeo = Nothing
        SyncLock Candado
            Try
                If escritor IsNot Nothing Then escritor.WriteLine("q")
            Catch
            End Try
            Try
                If cliente IsNot Nothing Then cliente.Close()
            Catch
            End Try
            cliente = Nothing : lector = Nothing : escritor = Nothing
        End SyncLock
        Try
            If proceso IsNot Nothing AndAlso Not proceso.HasExited Then
                proceso.Kill()
                proceso.WaitForExit(2000)
            End If
        Catch
        End Try
        proceso = Nothing
    End Sub

    ' ------------------------------------------------------------------
    ' Comandos
    ' ------------------------------------------------------------------
    ' Envia un comando y devuelve la primera linea de respuesta ("" si fallo)
    Private Shared Function Comando(texto As String) As String
        SyncLock Candado
            If escritor Is Nothing Then Return ""
            Try
                escritor.WriteLine(texto)
                Dim r As String = lector.ReadLine()
                Return If(r, "").Trim()
            Catch ex As Exception
                UltimoError = ex.Message
                Return ""
            End Try
        End SyncLock
    End Function

    Public Shared Function LeerFrecuencia() As Long
        Dim r As String = Comando("f")
        Dim hz As Double
        If Double.TryParse(r, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, hz) AndAlso hz > 0 Then
            FrecuenciaHz = CLng(hz)
            Return FrecuenciaHz
        End If
        If r.StartsWith("RPRT") Then UltimoError = Tr("El radio respondio {0}", r)
        Return 0
    End Function

    Public Shared Function PonerFrecuencia(hz As Long) As Boolean
        Dim r As String = Comando("F " & hz.ToString())
        Dim ok As Boolean = (r = "RPRT 0")
        If ok Then FrecuenciaHz = hz
        Return ok
    End Function

    ' Modo que Pako le pone al radio al cambiar de banda (Configurar > Radio (CAT)):
    '   0 = USB    1 = DATA (PKTUSB: USB de datos, el audio entra por atras o por USB)    2 = no tocar el modo
    Public Shared Property ModoRadio As Integer
        Get
            Try
                Return Math.Max(0, Math.Min(2, CInt(Val(GetSetting("Pako", "Opciones", "CatModo", "0")))))
            Catch
                Return 0
            End Try
        End Get
        Set(value As Integer)
            Try
                SaveSetting("Pako", "Opciones", "CatModo", value.ToString())
            Catch
            End Try
        End Set
    End Property

    Public Shared Function PonerModoUsb() As Boolean
        Select Case ModoRadio
            Case 1 : Return Comando("M PKTUSB 0") = "RPRT 0"
            Case 2 : Return True
            Case Else : Return Comando("M USB 0") = "RPRT 0"
        End Select
    End Function

    ' PTT por CAT: el radio pasa a transmitir con un comando, sin usar RTS ni DTR
    Public Shared Function PonerPtt(encendido As Boolean) As Boolean
        Dim r As String = Comando(If(encendido, "T 1", "T 0"))
        Dim ok As Boolean = (r = "RPRT 0")
        If Not ok AndAlso r.StartsWith("RPRT") Then UltimoError = Tr("El radio respondio {0}", r)
        Trafico.Registrar("CAT", If(ok, TipoTrafico.Envio, TipoTrafico.[Error]), "PTT " & If(encendido, "ON", "OFF") & If(ok, "", "  -> " & r))
        Return ok
    End Function

    ' Cada segundo: leer la frecuencia del radio
    Private Shared Sub Sondear(estado As Object)
        If Not Conectado Then Return
        Dim antes As Long = FrecuenciaHz
        Dim hz As Long = LeerFrecuencia()
        If hz > 0 Then
            errores = 0
            If hz <> antes Then RaiseEvent FrecuenciaCambio(hz)
        Else
            errores += 1
            If errores >= 5 Then
                Dim motivo As String = Tr("El radio dejo de responder por CAT.") & " " & UltimoError
                Trafico.Registrar("CAT", TipoTrafico.Error, motivo)
                Desconectar()
                RaiseEvent Desconectado(motivo)
            End If
        End If
    End Sub

    ' ------------------------------------------------------------------
    ' Split "Fake It": mover el VFO para que el tono de audio quede entre 1500 y 2000 Hz
    ' Devuelve cuanto hay que mover el VFO (Hz) para una frecuencia de audio TX dada.
    ' ------------------------------------------------------------------
    Public Shared Function DesplazamientoFakeIt(audioTxHz As Double) As Integer
        If audioTxHz >= 1500 AndAlso audioTxHz < 2000 Then Return 0
        Return CInt(Math.Floor((audioTxHz - 1500) / 500.0) * 500)
    End Function
End Class
