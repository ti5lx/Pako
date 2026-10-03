Imports System.IO
Imports System.Text.RegularExpressions

' ======================================================================
' Paises (entidades DXCC) a partir de cty.dat (de country-files.com, AD1C)
' y registro de lo ya trabajado (indicativos, paises y grids) desde el log.
' Sirve para los colores de "nuevo pais", "nuevo grid", "ya trabajado".
' ======================================================================
Public Class Paises
    Private Shared exactos As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private Shared prefijos As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Public Shared Property Cargado As Boolean = False
    Public Shared Property Ruta As String = ""

    Private Shared ReadOnly Modificadores As New Regex("[\(\[<\{~].*$")

    ' Busca cty.dat junto al programa o en Documentos
    Public Shared Function BuscarArchivo() As String
        For Each r In {Path.Combine(Application.StartupPath, "cty.dat"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cty.dat")}
            If File.Exists(r) Then Return r
        Next
        Return ""
    End Function

    Public Shared Function Cargar(archivo As String) As Boolean
        Dim nuevosExactos As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim nuevosPrefijos As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim pais As String = ""
            For Each linea In File.ReadAllLines(archivo)
                If linea.Trim() = "" Then Continue For
                If Not Char.IsWhiteSpace(linea(0)) AndAlso linea.Contains(":") Then
                    ' Linea de entidad: "Japan:  25:  45:  AS:  36.40:  -138.38:  -9.0:  JA:"
                    pais = linea.Split(":"c)(0).Trim()
                    Continue For
                End If
                If pais = "" Then Continue For
                For Each pieza In linea.Replace(";", "").Split(","c)
                    Dim p As String = Modificadores.Replace(pieza.Trim(), "")
                    If p = "" Then Continue For
                    If p.StartsWith("=") Then
                        nuevosExactos(p.Substring(1)) = pais
                    Else
                        nuevosPrefijos(p) = pais
                    End If
                Next
            Next
        Catch
            Return False
        End Try
        If nuevosPrefijos.Count = 0 Then Return False
        exactos = nuevosExactos
        prefijos = nuevosPrefijos
        Ruta = archivo
        Cargado = True
        Return True
    End Function

    ' Pais de un indicativo ("" si no se sabe)
    Public Shared Function Buscar(indicativo As String) As String
        If Not Cargado OrElse String.IsNullOrEmpty(indicativo) Then Return ""
        Dim ex = exactos, pre = prefijos
        Dim nombre As String = Nothing
        If ex.TryGetValue(indicativo, nombre) Then Return nombre
        Dim c As String = Base(indicativo)
        If ex.TryGetValue(c, nombre) Then Return nombre
        For largo As Integer = c.Length To 1 Step -1
            If pre.TryGetValue(c.Substring(0, largo), nombre) Then Return nombre
        Next
        Return ""
    End Function

    ' La parte del indicativo que decide el pais: "KH6/W1ABC" -> KH6, "EA8/DL1XX/P" -> EA8, "W1ABC/P" -> W1ABC
    Private Shared Function Base(indicativo As String) As String
        Dim partes = indicativo.ToUpper().Split("/"c).
            Where(Function(p) p <> "" AndAlso Not {"P", "M", "MM", "AM", "QRP", "R", "A", "B", "LH"}.Contains(p) AndAlso
                              Not (p.Length = 1 AndAlso Char.IsDigit(p(0)))).ToList()
        If partes.Count = 0 Then Return indicativo.ToUpper()
        If partes.Count = 1 Then Return partes(0)
        Return If(partes(1).Length < partes(0).Length, partes(1), partes(0))
    End Function
End Class

' ----------------------------------------------------------------------
' Que tan "nuevo" es lo que se escucha
' ----------------------------------------------------------------------
Public Enum Novedad
    Ninguna = 0           ' ya trabajado en esta banda
    OtraBanda = 1         ' indicativo trabajado, pero en otra banda
    NuevoIndicativo = 2   ' nunca trabajado
    NuevoGrid = 3         ' grid nunca trabajado
    NuevoPaisBanda = 4    ' pais trabajado, pero no en esta banda
    NuevoPais = 5         ' pais nunca trabajado
End Enum

Public Class Trabajados
    Private Shared ReadOnly Candado As New Object()
    Private Shared indicativos As New HashSet(Of String)
    Private Shared indicativoBanda As New HashSet(Of String)
    Private Shared paisesTrab As New HashSet(Of String)
    Private Shared paisBandaTrab As New HashSet(Of String)
    Private Shared grids As New HashSet(Of String)
    Public Shared Property CantidadQsos As Integer = 0

    Public Shared ReadOnly Property CantidadPaises As Integer
        Get
            SyncLock Candado
                Return paisesTrab.Count
            End SyncLock
        End Get
    End Property

    ' Carga uno o varios logs ADIF (reemplaza lo anterior)
    Public Shared Sub Cargar(archivos As IEnumerable(Of String))
        Dim ind As New HashSet(Of String), indB As New HashSet(Of String)
        Dim pai As New HashSet(Of String), paiB As New HashSet(Of String), gr As New HashSet(Of String)
        Dim n As Integer = 0
        For Each archivo In archivos
            If String.IsNullOrEmpty(archivo) OrElse Not File.Exists(archivo) Then Continue For
            Try
                For Each r In Adif.Registros(File.ReadAllText(archivo))
                    Dim c As String = Adif.Campo(r, "CALL").Trim().ToUpper()
                    If c = "" Then Continue For
                    Dim b As String = Adif.Campo(r, "BAND").Trim().ToLower()
                    Dim g As String = Adif.Campo(r, "GRIDSQUARE").Trim().ToUpper()
                    Anotar(c, b, g, ind, indB, pai, paiB, gr)
                    n += 1
                Next
            Catch
            End Try
        Next
        SyncLock Candado
            indicativos = ind : indicativoBanda = indB : paisesTrab = pai : paisBandaTrab = paiB : grids = gr
            CantidadQsos = n
        End SyncLock
    End Sub

    ' Un QSO nuevo (al registrarlo en el log)
    Public Shared Sub Agregar(indicativo As String, banda As String, grid As String)
        SyncLock Candado
            Anotar(indicativo.ToUpper(), banda.ToLower(), grid.ToUpper(), indicativos, indicativoBanda, paisesTrab, paisBandaTrab, grids)
            CantidadQsos += 1
        End SyncLock
    End Sub

    Private Shared Sub Anotar(c As String, b As String, g As String,
                              ind As HashSet(Of String), indB As HashSet(Of String),
                              pai As HashSet(Of String), paiB As HashSet(Of String), gr As HashSet(Of String))
        ind.Add(c)
        If b <> "" Then indB.Add(c & "|" & b)
        Dim p As String = Paises.Buscar(c)
        If p <> "" Then
            pai.Add(p)
            If b <> "" Then paiB.Add(p & "|" & b)
        End If
        If g.Length >= 4 Then gr.Add(g.Substring(0, 4))
    End Sub

    ' Cuan nuevo es un indicativo (y su grid, si lo trae) en una banda
    Public Shared Function Evaluar(indicativo As String, banda As String, grid As String, pais As String) As Novedad
        If String.IsNullOrEmpty(indicativo) Then Return Novedad.Ninguna
        SyncLock Candado
            If pais <> "" Then
                If Not paisesTrab.Contains(pais) Then Return Novedad.NuevoPais
                If banda <> "" AndAlso Not paisBandaTrab.Contains(pais & "|" & banda) Then Return Novedad.NuevoPaisBanda
            End If
            If Not String.IsNullOrEmpty(grid) AndAlso Not grids.Contains(grid) Then Return Novedad.NuevoGrid
            If Not indicativos.Contains(indicativo) Then Return Novedad.NuevoIndicativo
            If banda <> "" AndAlso Not indicativoBanda.Contains(indicativo & "|" & banda) Then Return Novedad.OtraBanda
            Return Novedad.Ninguna
        End SyncLock
    End Function
End Class
