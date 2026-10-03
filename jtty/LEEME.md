# JTTY en Pakö

Motor JTTY de WSJT-X 3.2.0-rc1 empaquetado como `jtty_pako.dll` para usarlo desde Pakö (VB.NET), igual que se hizo con ft8_lib.

## Archivos

| Archivo | Qué es |
|---|---|
| `jtty_pako.f90` | Envoltorio con 5 funciones exportadas en C (lo único nuevo en Fortran) |
| `compilar_jtty_windows.sh` | Descarga el código de JTTY y genera `jtty_pako.dll` en Windows con MSYS2 |
| `JttyMotor.vb` | Clase lista para Pakö: `Recibir()` y `Transmitir()` |
| `test_loopback.c` | Prueba: codifica un mensaje, le agrega ruido y lo decodifica |

## Pasos en Windows

1. Instalar MSYS2 (msys2.org) y abrir la terminal **MSYS2 UCRT64**.
2. `pacman -S --needed git mingw-w64-ucrt-x86_64-gcc-fortran mingw-w64-ucrt-x86_64-fftw`
3. En la carpeta con estos archivos: `bash compilar_jtty_windows.sh`
4. Copiar `jtty_pako.dll` junto a `Pako.exe`.
5. En Visual Studio: agregar `JttyMotor.vb` al proyecto y poner la plataforma en **x64**.

## Uso desde Pakö

```vb
Dim jtty As New JttyMotor()
jtty.FrecRxHz = 1500

' Recepción: cada ~100 ms con el audio recién capturado (12000/s, 16 bits)
For Each m In jtty.Recibir(bloqueAudio)
    ' El mismo m.Id llega varias veces mientras el texto crece:
    ' reemplazar la línea con ese Id en pantalla. m.Completo = fin de mensaje.
Next

' Transmisión
Dim enviado As String = ""
Dim onda = jtty.Transmitir("CQ TI2LX CQ", 1500, enviado)
If onda IsNot Nothing Then
    ' PTT por RTS, reproducir onda a 12000/s, soltar PTT
End If
```

## Datos del modo

- 4-GFSK, 31.25 baudios, 125 Hz de ancho de banda.
- Trama de 1.89 s; un mensaje de hasta 80 caracteres usa hasta 16 tramas.
- Sin ranuras de tiempo: se transmite en cualquier momento.

## Licencia

El código de JTTY es de WSJT-X, bajo GNU GPL v3. Al distribuir Pakö con esta DLL, Pakö debe publicarse también bajo GPL v3 con su código fuente.
