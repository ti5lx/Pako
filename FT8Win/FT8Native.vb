Imports System.Runtime.InteropServices

' Declaraciones para llamar a FT8Core.dll desde VB.NET
Public Module FT8Native

    ' Debe coincidir exactamente con FT8_RESULT en FT8Core.h
    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Ansi)>
    Public Structure FT8Result
        Public Snr As Single
        Public TimeSec As Single
        Public FreqHz As Single
        Public Score As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=40)>
        Public Text As String
    End Structure

    ' Modos para FT8_DecodeEx / FT8_EncodeEx
    Public Const FT8_MODO_FT8 As Integer = 0
    Public Const FT8_MODO_FT4 As Integer = 1

    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function FT8_Version() As Integer
    End Function

    ' --- Solo FT8 (se mantienen por compatibilidad) ---
    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function FT8_Decode(samples As Single(), numSamples As Integer, sampleRate As Integer,
                               <[In], Out> results As FT8Result(), maxResults As Integer) As Integer
    End Function

    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function FT8_Encode(text As String, frequencyHz As Single, sampleRate As Integer,
                               <Out> samples As Single(), maxSamples As Integer) As Integer
    End Function

    ' --- FT8 o FT4 (modo = FT8_MODO_FT8 o FT8_MODO_FT4) ---
    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function FT8_DecodeEx(samples As Single(), numSamples As Integer, sampleRate As Integer, modo As Integer,
                                 <[In], Out> results As FT8Result(), maxResults As Integer) As Integer
    End Function

    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function FT8_EncodeEx(text As String, frequencyHz As Single, sampleRate As Integer, modo As Integer,
                                 <Out> samples As Single(), maxSamples As Integer) As Integer
    End Function

    ' --- Decodificacion "a priori": mi indicativo, el del DX ("" si ninguno) y la frecuencia RX ---
    <DllImport("FT8Core.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub FT8_SetAP(micall As String, dxcall As String, frecRxHz As Single)
    End Sub

End Module
