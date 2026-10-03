# Pakö — El comunicador Bribri

**Pakö** («comunicar» en idioma bribri) es un programa libre para radioaficionados que permite hacer contactos en los modos digitales **FT8**, **FT4** y **JTTY**. Escrito por **TI2LX** en Costa Rica.

*Pakö ("to communicate" in the Bribri language) is free software for amateur radio: FT8, FT4 and JTTY for Windows, written by TI2LX in Costa Rica. The interface is available in Spanish, English, German, Portuguese, Russian, Chinese and Japanese.*

## Qué hace

- Recibe y transmite FT8, FT4 y JTTY, con secuencia automática del QSO.
- Decodificación de señales débiles con varias pasadas, OSD y AP.
- Cascada (waterfall) con clic para elegir frecuencia de RX y TX.
- Colores para país nuevo, país nuevo en la banda, grid nuevo y ya trabajado.
- Control del radio por CAT con Hamlib (misma lista de radios que WSJT-X) y split.
- PTT por puerto serie: RTS, DTR o ambos.
- Log en ADIF y subida automática a QRZ.com, eQSL y LoTW.
- Reportes a PSKReporter.
- Macros editables para JTTY (F1 a F8 y Shift+F1 a F8), CQ automático y número de serie.
- Siete idiomas.

## Instalar

Descarga el instalador desde la sección **Releases** de este repositorio y ejecútalo. Requiere Windows 10 u 11 de 64 bits.

## Compilar

Necesitas Visual Studio 2019 o posterior, con Visual Basic .NET (.NET Framework 4.7.2) y C++.

1. Abre la solución `FT8App.sln`.
2. Elige la configuración **Release** y la plataforma **x64**.
3. Compila la solución. El resultado queda en `FT8Win\bin\Release`.

Estructura:

| Carpeta | Contenido |
| --- | --- |
| `FT8Win` | El programa (Visual Basic .NET, WinForms). El ensamblado se llama `Pako.exe`. |
| `FT8Core` | DLL en C++ con el motor de FT8 y FT4, basada en ft8_lib. |
| `jtty` | Envoltorio `jtty_pako.f90` y script para compilar `jtty_pako.dll` con MSYS2. |
| `instalador` | Script de Inno Setup y licencia. |

### Compilar jtty_pako.dll

El modo JTTY usa el código de WSJT-X 3.2.0-rc1 (`lib/jtty`). En una terminal **MSYS2 UCRT64**:

```
pacman -S --needed git mingw-w64-ucrt-x86_64-gcc-fortran mingw-w64-ucrt-x86_64-fftw
cd jtty
bash compilar_jtty_windows.sh
```

El script descarga el código de WSJT-X desde https://github.com/WSJTX/wsjtx (etiqueta `v3.2.0-rc1`) y genera `jtty_pako.dll`. Cópiala junto a `Pako.exe`.

## Licencia

Pakö es software libre bajo la **GNU General Public License versión 3** (o posterior). Ver el archivo [LICENSE](LICENSE).

Copyright (C) 2026 TI2LX.

Se distribuye SIN NINGUNA GARANTÍA. Cada operador es responsable de sus transmisiones y de cumplir las normas de su país.

Si distribuyes una versión modificada, por favor usa otro nombre para que no se confunda con la original.

## Créditos

- **Modo JTTY:** código de WSJT-X 3.2.0-rc1, GPL v3.

  > The algorithms, source code, look-and-feel of WSJT-X, MAP65, QMAP, and related programs, and protocol specifications for the modes FSK441, FST4, FST4W, FT4, FT8, ISCAT, JT4, JT6M, JT9, JT65, JTMS, JTTY, MSK144, QRA64, Q65, and WSPR are Copyright (C) 2001-2026 by one or more of the following authors: Joseph Taylor, K1JT; Bill Somerville, G4WJS; Steven Franke, K9AN; Nico Palermo, IV3NWV; Greg Beam, KI7MT; Michael Black, W9MDB; Edson Pereira, PY2SDR; Philip Karn, KA9Q; Uwe Risse, DG2YCB; Brian Moran, N9ADG; Roger Rehr, W3SZ; John Nelson, G4KLA; Charlie Suckling, DL3WDG; Terrell Deppe, KJ5HST; David Christle, KD0BTO; and other members of the WSJT Development Team.

- **ft8_lib** de Kārlis Goba (licencia MIT): https://github.com/kgoba/ft8_lib
- **NAudio** de Mark Heath (licencia MIT): https://github.com/naudio/NAudio
- **Hamlib** (LGPL), usado como programa aparte: https://github.com/Hamlib/Hamlib
- **FFTW** (GPL), incluida en `jtty_pako.dll`.
- **cty.dat** de Jim Reisert AD1C: https://www.country-files.com

73 de TI2LX
