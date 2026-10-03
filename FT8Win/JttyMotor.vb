' JttyMotor.vb -- Acceso al modo JTTY desde Pako (VB.NET) mediante jtty_pako.dll
' Pako V.1.0 "El comunicador Bribri" - TI2LX
' Licencia: GNU GPL v3 (porque enlaza código de WSJT-X).
'
' Requisitos:
'  - jtty_pako.dll (64 bits) junto al .exe de Pako
'  - Proyecto compilado para x64 (no "Any CPU" con "Preferir 32 bits")
'  - Audio mono a 12000 muestras/s (igual que FT8)
'  - Llamar al motor siempre desde el mismo hilo: el decodificador no es
'    seguro para varios hilos a la vez.

Imports System.Runtime.InteropServices
Imports System.Text

Friend NotInheritable Class JttyNativo
    Private Const DLL As String = "jtty_pako.dll"

    <DllImport(DLL, CallingConvention:=CallingConvention.Cdecl)>
    Friend Shared Function jtty_pako_version() As Integer
    End Function

    <DllImport(DLL, CallingConvention:=CallingConvention.Cdecl)>
    Friend Shared Sub jtty_pako_reset()
    End Sub

    <DllImport(DLL, CallingConvention:=CallingConvention.Cdecl)>
    Friend Shared Function jtty_pako_feed(samples As Short(), n As Integer,
                                          nfa As Integer, nfb As Integer,
                                          f0 As Single, ftol As Single) As Integer
    End Function

    <DllImport(DLL, CallingConvention:=CallingConvention.Cdecl)>
    Friend Shared Function jtty_pako_get_updates(<Out> text As Byte(), <Out> ids As Long(),
                                                 <Out> freq As Single(), <Out> tstart As Single(),
                                                 <Out> complete As Integer()) As Integer
    End Function

    <DllImport(DLL, CallingConvention:=CallingConvention.Cdecl)>
    Friend Shared Function jtty_pako_encode(msg As Byte(), msglen As Integer, f0 As Single,
                                            <Out> wave As Single(), maxwave As Integer,
                                            <Out> canon As Byte()) As Integer
    End Function
End Class

''' <summary>Un mensaje recibido. El mismo Id llega varias veces mientras el texto crece.</summary>
Public Class JttyMensaje
    Public Property Id As Long
    Public Property FrecuenciaHz As Single
    Public Property Texto As String
    Public Property InicioSeg As Single
    Public Property Completo As Boolean      ' True cuando llegó el fin de mensaje
End Class

Public Class JttyMotor
    Public Const TasaMuestreo As Integer = 12000
    Private Const LOTE As Integer = 30
    Private Const LARGO As Integer = 80
    Private Shared ReadOnly Latin1 As Encoding = Encoding.GetEncoding("ISO-8859-1")
    Private Const MAX_MUESTRAS_TX As Integer = 16 * 59 * 384   ' 16 tramas = 30.2 s

    ' Ventana de búsqueda y frecuencia de recepción (como en WSJT-X)
    Public Property FrecMinHz As Integer = 200
    Public Property FrecMaxHz As Integer = 3000
    Public Property FrecRxHz As Single = 1500
    Public Property ToleranciaHz As Single = 50

    Public Sub New()
        If JttyNativo.jtty_pako_version() <> 1 Then
            Throw New InvalidOperationException("Versión de jtty_pako.dll no esperada")
        End If
        JttyNativo.jtty_pako_reset()
    End Sub

    ''' <summary>Borra el audio acumulado y los mensajes a medias.</summary>
    Public Sub Reiniciar()
        JttyNativo.jtty_pako_reset()
    End Sub

    ''' <summary>
    ''' Entrega audio nuevo (12000/s, 16 bits) y devuelve lo decodificado que
    ''' es nuevo o creció. Ideal: llamarlo cada ~100 ms con el bloque recién capturado.
    ''' </summary>
    Public Function Recibir(muestras As Short(), Optional cuantas As Integer = -1) As List(Of JttyMensaje)
        If cuantas < 0 Then cuantas = muestras.Length
        JttyNativo.jtty_pako_feed(muestras, cuantas, FrecMinHz, FrecMaxHz, FrecRxHz, ToleranciaHz)
        Return LeerNovedades()
    End Function

    Private Function LeerNovedades() As List(Of JttyMensaje)
        Dim lista As New List(Of JttyMensaje)
        Dim texto(LOTE * LARGO - 1) As Byte
        Dim ids(LOTE - 1) As Long
        Dim frec(LOTE - 1) As Single
        Dim inicio(LOTE - 1) As Single
        Dim fin(LOTE - 1) As Integer
        Dim n As Integer
        Do
            n = JttyNativo.jtty_pako_get_updates(texto, ids, frec, inicio, fin)
            For i As Integer = 0 To n - 1
                If ids(i) <= 0 Then Continue For
                lista.Add(New JttyMensaje With {
                    .Id = ids(i),
                    .FrecuenciaHz = frec(i),
                    .Texto = Latin1.GetString(texto, i * LARGO, LARGO).Trim(),
                    .InicioSeg = inicio(i),
                    .Completo = (fin(i) <> 0)})
            Next
        Loop While n = LOTE
        Return lista
    End Function

    ''' <summary>
    ''' Genera el audio para transmitir. Devuelve Nothing si el texto no se puede enviar.
    ''' textoEnviado devuelve el mensaje tal como saldrá (mayúsculas, '#' en lugar
    ''' de caracteres no soportados), útil para mostrarlo y para el log.
    ''' </summary>
    Public Function Transmitir(mensaje As String, frecTxHz As Single,
                               ByRef textoEnviado As String) As Single()
        Dim bytes = Latin1.GetBytes(If(mensaje, "").ToUpperInvariant())
        Dim onda(MAX_MUESTRAS_TX - 1) As Single
        Dim canon(LARGO - 1) As Byte
        Dim n = JttyNativo.jtty_pako_encode(bytes, bytes.Length, frecTxHz, onda, onda.Length, canon)
        textoEnviado = Latin1.GetString(canon).Trim()
        If n <= 0 Then Return Nothing
        Array.Resize(onda, n)
        Return onda          ' muestras -1..+1 a 12000/s, listas para la tarjeta de sonido
    End Function
End Class
