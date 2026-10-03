#!/bin/bash
# compilar_jtty_windows.sh -- Genera jtty_pako.dll (64 bits) para Pako.
#
# Se ejecuta en Windows dentro de MSYS2, en la terminal "MSYS2 UCRT64".
# Visual Studio no compila Fortran, por eso se usa gfortran de MSYS2.
#
# Una sola vez, instalar compilador, FFTW y git:
#   pacman -S --needed git mingw-w64-ucrt-x86_64-gcc-fortran mingw-w64-ucrt-x86_64-fftw
#
# Luego, en la carpeta donde están jtty_pako.f90 y este script:
#   bash compilar_jtty_windows.sh
set -e

ETIQUETA=v3.2.0-rc1            # versión de WSJT-X de donde sale JTTY
AQUI=$(pwd)
SRC=$AQUI/wsjtx
OBJ=$AQUI/obj

if [ ! -d "$SRC" ]; then
  git clone --depth 1 --branch $ETIQUETA https://github.com/WSJTX/wsjtx.git "$SRC"
fi
L=$SRC/lib
rm -rf "$OBJ" && mkdir -p "$OBJ" && cd "$OBJ"

# Interfaz Fortran de FFTW que trae MSYS2
FFTW_INC=$(dirname "$(find /ucrt64/include -name fftw3.f03 | head -1)")

# Archivos de lib/jtty (sin los programas de consola ni su bucle de audio)
ARCHIVOS="$L/fftw3mod.f90 $L/77bit/packjt77_schema.f90 $L/77bit/packjt77_grammar.f90
          $L/77bit/packjt77.f90 $L/wavhdr.f90"
for f in $L/jtty/*.f90; do
  case $(basename $f) in
    jtty.f90|rjtty.f90|sjtty.f90|sjtty_qrm.f90|update.f90|transmit.f90|c_funcs.f90|jtty_codewords.f90) ;;
    *) ARCHIVOS="$ARCHIVOS $f" ;;
  esac
done
# Rutinas auxiliares de WSJT-X que JTTY usa
for f in chkcall db four2a gfsk_pulse smo121 twkfreq; do ARCHIVOS="$ARCHIVOS $L/$f.f90"; done
ARCHIVOS="$ARCHIVOS $AQUI/jtty_pako.f90"

OPC="-c -O2 -I$FFTW_INC -I$L -I$L/77bit -J."

# Los módulos dependen unos de otros: se compila en varias pasadas
# hasta que todo quede listo.
PEND="$ARCHIVOS"
for pasada in 1 2 3 4 5 6 7 8 9 10; do
  SIGUE=""
  for f in $PEND; do
    gfortran $OPC "$f" -o "$(basename "${f%.f90}").o" 2>/dev/null || SIGUE="$SIGUE $f"
  done
  [ -z "$SIGUE" ] && break
  [ "$SIGUE" = "$PEND" ] && break
  PEND="$SIGUE"
done
if [ -n "$SIGUE" ]; then
  echo "No compilaron:"; for f in $SIGUE; do echo "  $f"; gfortran $OPC "$f" -o /dev/null; done
  exit 1
fi

# DLL con todo incluido (gfortran y FFTW estáticos): un solo archivo para Pako
gfortran -shared -o "$AQUI/jtty_pako.dll" *.o \
  -static -lfftw3f -Wl,--no-undefined

echo
echo "Listo: $AQUI/jtty_pako.dll"
echo "Copiala junto a Pako.exe (proyecto compilado en x64)."
objdump -p "$AQUI/jtty_pako.dll" | grep -E "DLL Name|jtty_pako_" | sed 's/^/  /'
