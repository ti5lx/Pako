Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

' ======================================================================
' SUBIDA DE QSOs A QRZ.com, eQSL y LoTW
' Basado en CARGALOG (TI2LX), adaptado a Pako.
' ======================================================================

Public Enum EstadoSubida
    Subido
    Duplicado
    ErrorAutorizacion
    ErrorRed
    [Error]
End Enum

Public Class ResultadoSubida
    Public Estado As EstadoSubida = EstadoSubida.Error
    Public Mensaje As String = ""
    Public Total As Integer = 0
    Public Subidos As Integer = 0
    Public Duplicados As Integer = 0
    Public Errores As Integer = 0

    ' Hay que guardarlo para reintentar despues (fallo de red o del servidor)
    Public ReadOnly Property Reintentar As Boolean
        Get
            Return Estado = EstadoSubida.ErrorRed OrElse Estado = EstadoSubida.Error OrElse
                   Estado = EstadoSubida.ErrorAutorizacion
        End Get
    End Property

    Public Function Resumen() As String
        Select Case Estado
            Case EstadoSubida.Subido : Return Tr("subido")
            Case EstadoSubida.Duplicado : Return Tr("ya estaba subido")
            Case EstadoSubida.ErrorAutorizacion : Return Tr("credenciales incorrectas: {0}", Mensaje)
            Case EstadoSubida.ErrorRed : Return Tr("sin conexion (queda pendiente)")
            Case Else : Return Tr("error: {0}", Mensaje)
        End Select
    End Function
End Class

' ----------------------------------------------------------------------
' Credenciales cifradas con DPAPI: solo tu usuario de Windows puede leerlas
' ----------------------------------------------------------------------
Public Module Credenciales
    Private ReadOnly Entropia As Byte() = Encoding.UTF8.GetBytes("Pako-TI2LX-servicios")
    Private Const Prefijo As String = "dpapi:"

    Public Function Cifrar(texto As String) As String
        If String.IsNullOrEmpty(texto) Then Return ""
        Dim datos = ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), Entropia, DataProtectionScope.CurrentUser)
        Return Prefijo & Convert.ToBase64String(datos)
    End Function

    Public Function Descifrar(guardado As String) As String
        If String.IsNullOrEmpty(guardado) Then Return ""
        If Not guardado.StartsWith(Prefijo) Then Return guardado   ' texto plano antiguo
        Try
            Dim datos = Convert.FromBase64String(guardado.Substring(Prefijo.Length))
            Return Encoding.UTF8.GetString(ProtectedData.Unprotect(datos, Entropia, DataProtectionScope.CurrentUser))
        Catch
            Return ""
        End Try
    End Function
End Module

' ----------------------------------------------------------------------
' Utilidades ADIF
' ----------------------------------------------------------------------
Public Module Adif
    Public Function Encabezado() As String
        Return "Pako log" & vbCrLf & "<ADIF_VER:5>3.1.4 <PROGRAMID:4>Pako <EOH>" & vbCrLf
    End Function

    ' Separa un archivo ADI en registros (cada uno termina en <EOR>)
    Public Function Registros(adif As String) As List(Of String)
        Dim lista As New List(Of String)
        If String.IsNullOrWhiteSpace(adif) Then Return lista
        Dim eoh As Match = Regex.Match(adif, "(?i)<\s*EOH\s*>")
        If eoh.Success Then adif = adif.Substring(eoh.Index + eoh.Length)
        For Each m As Match In Regex.Matches(adif, "(?is).*?<\s*EOR\s*>")
            Dim r As String = Regex.Replace(m.Value.Trim(), "(?i)<\s*EOR\s*>", "<EOR>")
            If r.Length > 0 Then lista.Add(r)
        Next
        Return lista
    End Function

    ' Valor de un campo en un registro (ej. CALL)
    Public Function Campo(registro As String, nombre As String) As String
        Dim m As Match = Regex.Match(registro, "(?i)<\s*" & nombre & "\s*:\s*(\d+)(?::[A-Z])?\s*>")
        If Not m.Success Then Return ""
        Dim largo As Integer = CInt(m.Groups(1).Value)
        Dim inicio As Integer = m.Index + m.Length
        If inicio + largo > registro.Length Then Return ""
        Return registro.Substring(inicio, largo)
    End Function
End Module

' ----------------------------------------------------------------------
' Cola de pendientes: QSOs que no se pudieron subir (sin internet, etc.)
' Un archivo por servicio en Documentos: Pako_pendientes_QRZ.adi, etc.
' ----------------------------------------------------------------------
Public Module ColaPendientes
    Private ReadOnly Candado As New Object()

    Public Function Ruta(servicio As String) As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "Pako_pendientes_" & servicio & ".adi")
    End Function

    Public Sub Agregar(servicio As String, registro As String)
        SyncLock Candado
            Dim r As String = Ruta(servicio)
            If Not File.Exists(r) Then File.WriteAllText(r, Adif.Encabezado(), New UTF8Encoding(False))
            File.AppendAllText(r, registro & vbCrLf, New UTF8Encoding(False))
        End SyncLock
    End Sub

    Public Function Leer(servicio As String) As List(Of String)
        SyncLock Candado
            Dim r As String = Ruta(servicio)
            If Not File.Exists(r) Then Return New List(Of String)
            Return Adif.Registros(File.ReadAllText(r, Encoding.UTF8))
        End SyncLock
    End Function

    Public Sub Reescribir(servicio As String, registros As List(Of String))
        SyncLock Candado
            Dim r As String = Ruta(servicio)
            If registros.Count = 0 Then
                If File.Exists(r) Then File.Delete(r)
                Return
            End If
            Dim sb As New StringBuilder(Adif.Encabezado())
            For Each reg In registros
                sb.AppendLine(reg)
            Next
            File.WriteAllText(r, sb.ToString(), New UTF8Encoding(False))
        End SyncLock
    End Sub

    Public Function Cantidad(servicio As String) As Integer
        Return Leer(servicio).Count
    End Function
End Module

' ======================================================================
' TRAFICO: todo lo que Pako envia a los servidores y lo que responden.
' Se muestra en Log > Ver trafico, y se guarda en Documentos\Pako_trafico.log
' Las claves se ocultan (****) antes de guardarlas o mostrarlas.
' ======================================================================
Public Enum TipoTrafico
    Envio
    Respuesta
    [Error]
    Info
End Enum

Public Class EntradaTrafico
    Public Hora As DateTime
    Public Servicio As String
    Public Tipo As TipoTrafico
    Public Texto As String
End Class

Public NotInheritable Class Trafico
    Private Shared ReadOnly Lista As New List(Of EntradaTrafico)
    Private Shared ReadOnly Candado As New Object()
    Private Const Maximo As Integer = 1000

    Public Shared Event Nueva(entrada As EntradaTrafico)

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property RutaArchivo As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pako_trafico.log")
        End Get
    End Property

    Public Shared Sub Registrar(servicio As String, tipo As TipoTrafico, texto As String)
        Dim e As New EntradaTrafico With {.Hora = DateTime.UtcNow, .Servicio = servicio, .Tipo = tipo, .Texto = texto}
        SyncLock Candado
            Lista.Add(e)
            If Lista.Count > Maximo Then Lista.RemoveAt(0)
            Try
                File.AppendAllText(RutaArchivo,
                    String.Format("[{0:yyyy-MM-dd HH:mm:ss} UTC] {1} {2}{3}{4}{3}{3}",
                                  e.Hora, servicio, NombreTipo(tipo), vbCrLf, texto),
                    New UTF8Encoding(False))
            Catch
            End Try
        End SyncLock
        RaiseEvent Nueva(e)
    End Sub

    Public Shared Function Historial() As List(Of EntradaTrafico)
        SyncLock Candado
            Return New List(Of EntradaTrafico)(Lista)
        End SyncLock
    End Function

    Public Shared Sub Limpiar()
        SyncLock Candado
            Lista.Clear()
        End SyncLock
    End Sub

    Public Shared Function NombreTipo(t As TipoTrafico) As String
        Select Case t
            Case TipoTrafico.Envio : Return ">>> " & Tr("ENVIADO")
            Case TipoTrafico.Respuesta : Return "<<< " & Tr("RESPUESTA")
            Case TipoTrafico.Error : Return "!!! " & Tr("ERROR")
            Case Else : Return "--- INFO"
        End Select
    End Function

    ' Muestra solo los ultimos 4 caracteres de una clave
    Public Shared Function Ocultar(clave As String) As String
        If String.IsNullOrEmpty(clave) Then Return "(vacia)"
        If clave.Length <= 4 Then Return "****"
        Return "****" & clave.Substring(clave.Length - 4)
    End Function

    ' Evita llenar la ventana con logs enormes
    Public Shared Function Recortar(texto As String, Optional maximo As Integer = 4000) As String
        If texto Is Nothing Then Return ""
        If texto.Length <= maximo Then Return texto
        Return texto.Substring(0, maximo) & vbCrLf & "... (" & (texto.Length - maximo) & " caracteres mas)"
    End Function
End Class

' ======================================================================
' CLIENTES
' ======================================================================
Public NotInheritable Class ServiciosLog

    Private Shared ReadOnly Http As New HttpClient()
    Private Shared ReadOnly FilaLotw As New SemaphoreSlim(1, 1)   ' TQSL: de a uno por vez

    Shared Sub New()
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Pako/" & Form1.VersionPrograma & " (TI2LX)")
        Http.Timeout = TimeSpan.FromSeconds(30)
    End Sub

    Private Sub New()
    End Sub

    ' ------------------------------------------------------------------
    ' QRZ.com  (https://logbook.qrz.com/api, un QSO por llamada)
    ' ------------------------------------------------------------------
    Public Shared Async Function QrzSubirAsync(apiKey As String, registro As String) As Task(Of ResultadoSubida)
        Dim r As New ResultadoSubida With {.Total = 1}
        Try
            Dim datos As New Dictionary(Of String, String) From {
                {"KEY", apiKey.Trim()}, {"ACTION", "INSERT"}, {"ADIF", registro}}
            Trafico.Registrar("QRZ", TipoTrafico.Envio,
                "POST https://logbook.qrz.com/api" & vbCrLf &
                "KEY=" & Trafico.Ocultar(apiKey.Trim()) & vbCrLf &
                "ACTION=INSERT" & vbCrLf &
                "ADIF=" & registro)
            Using contenido As HttpContent = Formulario(datos)
                Dim resp = Await Http.PostAsync("https://logbook.qrz.com/api", contenido)
                Dim texto As String = Await resp.Content.ReadAsStringAsync()
                Trafico.Registrar("QRZ", TipoTrafico.Respuesta,
                    "HTTP " & CInt(resp.StatusCode) & " " & resp.ReasonPhrase & vbCrLf &
                    Uri.UnescapeDataString(texto.Replace("&", vbCrLf).Replace("+", " ")))
                If Not resp.IsSuccessStatusCode Then
                    r.Estado = EstadoSubida.ErrorRed
                    r.Mensaje = "HTTP " & CInt(resp.StatusCode)
                    Return r
                End If
                Dim v = LeerNombreValor(texto)
                Dim resultado As String = If(v.ContainsKey("RESULT"), v("RESULT").ToUpperInvariant(), "")
                r.Mensaje = If(v.ContainsKey("REASON"), v("REASON"), texto)
                Select Case resultado
                    Case "OK", "REPLACE"
                        r.Estado = EstadoSubida.Subido : r.Subidos = 1
                    Case "AUTH"
                        r.Estado = EstadoSubida.ErrorAutorizacion : r.Errores = 1
                    Case "FAIL"
                        If r.Mensaje.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0 Then
                            r.Estado = EstadoSubida.Duplicado : r.Duplicados = 1
                        Else
                            r.Estado = EstadoSubida.Error : r.Errores = 1
                        End If
                    Case Else
                        r.Estado = EstadoSubida.Error : r.Errores = 1
                End Select
            End Using
        Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is TaskCanceledException
            r.Estado = EstadoSubida.ErrorRed : r.Mensaje = ex.Message : r.Errores = 1
            Trafico.Registrar("QRZ", TipoTrafico.Error, "Sin conexion: " & MensajeCompleto(ex))
        Catch ex As Exception
            r.Estado = EstadoSubida.Error : r.Mensaje = ex.Message : r.Errores = 1
            Trafico.Registrar("QRZ", TipoTrafico.Error, MensajeCompleto(ex))
        End Try
        Return r
    End Function

    ' Mensaje de una excepcion con sus causas internas (ej. el error real de red)
    Private Shared Function MensajeCompleto(ex As Exception) As String
        Dim sb As New StringBuilder(ex.Message)
        Dim interna = ex.InnerException
        While interna IsNot Nothing
            sb.Append(vbCrLf & "  causa: " & interna.Message)
            interna = interna.InnerException
        End While
        Return sb.ToString()
    End Function

    ' Verifica la API key (ACTION=STATUS). Devuelve un texto para mostrar.
    Public Shared Async Function QrzProbarAsync(apiKey As String) As Task(Of String)
        Try
            Dim datos As New Dictionary(Of String, String) From {{"KEY", apiKey.Trim()}, {"ACTION", "STATUS"}}
            Trafico.Registrar("QRZ", TipoTrafico.Envio,
                "POST https://logbook.qrz.com/api" & vbCrLf &
                "KEY=" & Trafico.Ocultar(apiKey.Trim()) & vbCrLf & "ACTION=STATUS")
            Using contenido As HttpContent = Formulario(datos)
                Dim resp = Await Http.PostAsync("https://logbook.qrz.com/api", contenido)
                Dim textoResp As String = Await resp.Content.ReadAsStringAsync()
                Trafico.Registrar("QRZ", TipoTrafico.Respuesta,
                    "HTTP " & CInt(resp.StatusCode) & " " & resp.ReasonPhrase & vbCrLf &
                    Uri.UnescapeDataString(textoResp.Replace("&", vbCrLf).Replace("+", " ")))
                Dim v = LeerNombreValor(textoResp)
                If v.ContainsKey("RESULT") AndAlso v("RESULT").ToUpperInvariant() = "OK" Then
                    Dim s As String = Tr("Conexion correcta.")
                    If v.ContainsKey("CALLSIGN") Then s &= vbCrLf & "Logbook: " & v("CALLSIGN")
                    If v.ContainsKey("COUNT") Then s &= vbCrLf & Tr("QSOs en el logbook: {0}", v("COUNT"))
                    Return s
                End If
                Return Tr("No se pudo validar la API key.") & vbCrLf &
                       If(v.ContainsKey("REASON"), v("REASON"), Tr("Respuesta desconocida de QRZ."))
            End Using
        Catch ex As Exception
            Trafico.Registrar("QRZ", TipoTrafico.Error, MensajeCompleto(ex))
            Return Tr("Error de conexion: {0}", ex.Message)
        End Try
    End Function

    ' Sube varios registros a QRZ, uno por uno (informa el avance)
    Public Shared Async Function QrzSubirVariosAsync(apiKey As String, registros As List(Of String),
                                                    avance As IProgress(Of String),
                                                    pendientes As List(Of String)) As Task(Of ResultadoSubida)
        Dim total As New ResultadoSubida With {.Total = registros.Count, .Estado = EstadoSubida.Subido}
        For i As Integer = 0 To registros.Count - 1
            Dim r = Await QrzSubirAsync(apiKey, registros(i))
            total.Subidos += r.Subidos
            total.Duplicados += r.Duplicados
            total.Errores += r.Errores
            If r.Reintentar AndAlso pendientes IsNot Nothing Then pendientes.Add(registros(i))
            avance?.Report(Tr("QRZ: {0} de {1}  (subidos {2}, duplicados {3}, errores {4})",
                                         i + 1, registros.Count, total.Subidos, total.Duplicados, total.Errores))
            If r.Estado = EstadoSubida.ErrorAutorizacion Then
                ' Con la key mal, no tiene sentido seguir: el resto queda pendiente
                total.Estado = EstadoSubida.ErrorAutorizacion
                total.Mensaje = r.Mensaje
                If pendientes IsNot Nothing Then
                    For k As Integer = i + 1 To registros.Count - 1
                        pendientes.Add(registros(k))
                    Next
                End If
                Exit For
            End If
        Next
        If total.Estado <> EstadoSubida.ErrorAutorizacion AndAlso total.Errores > 0 Then total.Estado = EstadoSubida.Error
        Return total
    End Function

    ' Formulario x-www-form-urlencoded sin el limite de 64 KB de FormUrlEncodedContent
    ' (un log completo para eQSL puede ser mucho mas grande)
    Private Shared Function Formulario(datos As Dictionary(Of String, String)) As HttpContent
        Dim sb As New StringBuilder()
        For Each kv In datos
            If sb.Length > 0 Then sb.Append("&"c)
            sb.Append(WebUtility.UrlEncode(kv.Key)).Append("="c).Append(WebUtility.UrlEncode(kv.Value))
        Next
        Return New StringContent(sb.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded")
    End Function

    Private Shared Function LeerNombreValor(texto As String) As Dictionary(Of String, String)
        Dim v As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrWhiteSpace(texto) Then Return v
        For Each parte In texto.Split("&"c)
            Dim i As Integer = parte.IndexOf("="c)
            If i > 0 Then v(parte.Substring(0, i).Trim()) = Uri.UnescapeDataString(parte.Substring(i + 1).Replace("+", " ")).Trim()
        Next
        Return v
    End Function

    ' ------------------------------------------------------------------
    ' eQSL  (https://www.eqsl.cc/qslcard/ImportADIF.cfm, acepta varios QSOs)
    ' ------------------------------------------------------------------
    Public Shared Async Function EqslSubirAsync(usuario As String, clave As String, registros As List(Of String)) As Task(Of ResultadoSubida)
        Dim r As New ResultadoSubida With {.Total = registros.Count}
        Try
            Dim sb As New StringBuilder(Adif.Encabezado())
            For Each reg In registros
                sb.AppendLine(reg)
            Next
            Dim datos As New Dictionary(Of String, String) From {
                {"ADIFData", sb.ToString()}, {"EQSL_USER", usuario.Trim()}, {"EQSL_PSWD", clave}}
            Trafico.Registrar("eQSL", TipoTrafico.Envio,
                "POST https://www.eqsl.cc/qslcard/ImportADIF.cfm" & vbCrLf &
                "EQSL_USER=" & usuario.Trim() & vbCrLf &
                "EQSL_PSWD=****" & vbCrLf &
                "ADIFData (" & registros.Count & " QSO):" & vbCrLf & Trafico.Recortar(sb.ToString()))
            Using contenido As HttpContent = Formulario(datos)
                Dim resp = Await Http.PostAsync("https://www.eqsl.cc/qslcard/ImportADIF.cfm", contenido)
                Dim html As String = Await resp.Content.ReadAsStringAsync()
                If Not resp.IsSuccessStatusCode Then
                    r.Estado = EstadoSubida.ErrorRed : r.Mensaje = "HTTP " & CInt(resp.StatusCode)
                    Trafico.Registrar("eQSL", TipoTrafico.Respuesta,
                        "HTTP " & CInt(resp.StatusCode) & " " & resp.ReasonPhrase & vbCrLf & Trafico.Recortar(html))
                    Return r
                End If

                ' HTML -> texto
                Dim texto As String = Regex.Replace(html, "(?i)<br\s*/?>", vbCrLf)
                texto = WebUtility.HtmlDecode(Regex.Replace(texto, "<[^>]+>", "")).Trim()
                texto = Regex.Replace(texto, "(\r?\n\s*){3,}", vbCrLf & vbCrLf)   ' quitar lineas vacias de sobra
                r.Mensaje = texto
                Trafico.Registrar("eQSL", TipoTrafico.Respuesta,
                    "HTTP " & CInt(resp.StatusCode) & " " & resp.ReasonPhrase & vbCrLf & Trafico.Recortar(texto))

                Dim m As Match = Regex.Match(texto, "Result:\s*(\d+)\s+out\s+of\s+(\d+)\s+records\s+added", RegexOptions.IgnoreCase)
                If m.Success Then
                    r.Subidos = CInt(m.Groups(1).Value)
                    r.Total = CInt(m.Groups(2).Value)
                End If
                r.Duplicados = Regex.Matches(texto, "Bad record:\s*Duplicate", RegexOptions.IgnoreCase).Count
                r.Errores = Math.Max(0, Regex.Matches(texto, "Bad record:", RegexOptions.IgnoreCase).Count - r.Duplicados)

                If Regex.IsMatch(texto, "(?im)^\s*Error:") Then
                    ' Ej.: "Error: No match on eQSL_User/eQSL_Pswd"
                    r.Estado = If(Regex.IsMatch(texto, "(?i)eQSL_User|eQSL_Pswd|password"),
                                  EstadoSubida.ErrorAutorizacion, EstadoSubida.Error)
                    r.Mensaje = Regex.Match(texto, "(?im)^\s*Error:.*$").Value.Trim()
                ElseIf r.Errores > 0 Then
                    r.Estado = EstadoSubida.Error
                ElseIf r.Subidos > 0 Then
                    r.Estado = EstadoSubida.Subido
                ElseIf r.Duplicados > 0 Then
                    r.Estado = EstadoSubida.Duplicado
                Else
                    r.Estado = EstadoSubida.Error
                End If
            End Using
        Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is TaskCanceledException
            r.Estado = EstadoSubida.ErrorRed : r.Mensaje = ex.Message
            Trafico.Registrar("eQSL", TipoTrafico.Error, "Sin conexion: " & MensajeCompleto(ex))
        Catch ex As Exception
            r.Estado = EstadoSubida.Error : r.Mensaje = ex.Message
            Trafico.Registrar("eQSL", TipoTrafico.Error, MensajeCompleto(ex))
        End Try
        Return r
    End Function

    ' ------------------------------------------------------------------
    ' LoTW  (firma y envio con TQSL por linea de comandos)
    '   -q  sin ventanas          -u  subir a LoTW
    '   -a compliant  omitir duplicados y QSOs fuera de rango
    '   -d  sin dialogo de fechas -b/-e  rango de fechas (opcional)
    '   -l  Station Location
    ' ------------------------------------------------------------------
    Public Shared Async Function LotwSubirAsync(tqsl As String, ubicacion As String, desde As String, hasta As String,
                                               registros As List(Of String)) As Task(Of ResultadoSubida)
        Dim r As New ResultadoSubida With {.Total = registros.Count}
        If String.IsNullOrWhiteSpace(tqsl) OrElse Not File.Exists(tqsl) Then
            r.Estado = EstadoSubida.ErrorAutorizacion : r.Mensaje = Tr("no se encontro tqsl.exe (revisa Configurar > Servicios de log)")
            Trafico.Registrar("LoTW", TipoTrafico.Error, r.Mensaje)
            Return r
        End If
        If String.IsNullOrWhiteSpace(ubicacion) Then
            r.Estado = EstadoSubida.ErrorAutorizacion : r.Mensaje = Tr("falta la Station Location de TQSL")
            Trafico.Registrar("LoTW", TipoTrafico.Error, r.Mensaje)
            Return r
        End If

        Await FilaLotw.WaitAsync()
        Dim temporal As String = Path.Combine(Path.GetTempPath(), "Pako_LoTW_" & Guid.NewGuid().ToString("N") & ".adi")
        Try
            If Process.GetProcessesByName("tqsl").Length > 0 Then
                r.Estado = EstadoSubida.Error : r.Mensaje = Tr("TQSL esta abierto; cierralo para poder subir")
                Trafico.Registrar("LoTW", TipoTrafico.Error, r.Mensaje)
                Return r
            End If

            Dim sb As New StringBuilder(Adif.Encabezado())
            For Each reg In registros
                sb.AppendLine(reg)
            Next
            File.WriteAllText(temporal, sb.ToString(), New UTF8Encoding(False))

            Dim args As String = "-q -u -a compliant -d "
            If desde <> "" Then args &= "-b " & desde & " "
            If hasta <> "" Then args &= "-e " & hasta & " "
            args &= "-l """ & ubicacion & """ """ & temporal & """"

            Trafico.Registrar("LoTW", TipoTrafico.Envio,
                "Ejecutando TQSL (firma y envio a LoTW):" & vbCrLf &
                Chr(34) & tqsl & Chr(34) & " " & args & vbCrLf & vbCrLf &
                "Contenido (" & registros.Count & " QSO):" & vbCrLf & Trafico.Recortar(sb.ToString()))

            Dim psi As New ProcessStartInfo(tqsl, args) With {
                .UseShellExecute = False, .CreateNoWindow = True,
                .RedirectStandardOutput = True, .RedirectStandardError = True}
            Dim salida As New StringBuilder()
            Dim codigo As Integer
            Using p As New Process() With {.StartInfo = psi}
                Dim recibir As DataReceivedEventHandler =
                    Sub(s As Object, e As DataReceivedEventArgs)
                        If e.Data Is Nothing Then Return
                        SyncLock salida
                            salida.AppendLine(e.Data)
                        End SyncLock
                    End Sub
                AddHandler p.OutputDataReceived, recibir
                AddHandler p.ErrorDataReceived, recibir
                p.Start()
                p.BeginOutputReadLine()
                p.BeginErrorReadLine()
                Await Task.Run(Sub()
                                   If Not p.WaitForExit(120000) Then
                                       Try : p.Kill() : Catch : End Try
                                   End If
                                   p.WaitForExit()
                               End Sub)
                codigo = p.ExitCode
            End Using

            Dim texto As String
            SyncLock salida
                texto = salida.ToString()
            End SyncLock

            ' Codigos de salida de TQSL
            Select Case codigo
                Case 0
                    r.Estado = EstadoSubida.Subido : r.Subidos = registros.Count
                Case 8
                    r.Estado = EstadoSubida.Duplicado : r.Duplicados = registros.Count
                    r.Mensaje = Tr("ya firmados antes o fuera del rango de fechas")
                Case 9
                    r.Estado = EstadoSubida.Subido : r.Mensaje = Tr("algunos QSOs se omitieron (duplicados o fuera de rango)")
                    Dim m As Match = Regex.Match(texto, "Attempting\s+to\s+upload\s+(one|\d+)\s+QSO", RegexOptions.IgnoreCase)
                    If m.Success Then r.Subidos = If(m.Groups(1).Value.ToLower() = "one", 1, CInt(m.Groups(1).Value))
                    r.Duplicados = Math.Max(0, registros.Count - r.Subidos)
                Case 11
                    r.Estado = EstadoSubida.ErrorRed : r.Mensaje = Tr("no se pudo conectar con LoTW")
                Case 2
                    r.Estado = EstadoSubida.Error : r.Mensaje = Tr("LoTW rechazo el envio")
                Case 1
                    r.Estado = EstadoSubida.Error : r.Mensaje = Tr("operacion cancelada por TQSL")
                Case Else
                    r.Estado = EstadoSubida.Error
                    r.Mensaje = Tr("TQSL devolvio el codigo {0}", codigo) &
                                If(texto.Trim() <> "", ": " & texto.Trim().Split({vbLf}, StringSplitOptions.RemoveEmptyEntries).Last().Trim(), "")
            End Select
            If r.Estado = EstadoSubida.Error Then r.Errores = registros.Count
            Trafico.Registrar("LoTW", If(r.Estado = EstadoSubida.Subido OrElse r.Estado = EstadoSubida.Duplicado,
                                         TipoTrafico.Respuesta, TipoTrafico.Error),
                "Salida de TQSL:" & vbCrLf & If(texto.Trim() = "", "(sin texto)", Trafico.Recortar(texto.Trim())) & vbCrLf & vbCrLf &
                "Codigo de salida: " & codigo & "  ->  " & r.Resumen())
        Catch ex As Exception
            r.Estado = EstadoSubida.Error : r.Mensaje = ex.Message : r.Errores = registros.Count
            Trafico.Registrar("LoTW", TipoTrafico.Error, MensajeCompleto(ex))
        Finally
            Try
                If File.Exists(temporal) Then File.Delete(temporal)
            Catch
            End Try
            FilaLotw.Release()
        End Try
        Return r
    End Function
End Class
