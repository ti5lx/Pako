// FT8Core.h : funciones que la DLL exporta para VB.NET (P/Invoke)
#pragma once

#ifdef _WIN32
#define FT8_API extern "C" __declspec(dllexport)
#else
#define FT8_API extern "C" __attribute__((visibility("default")))
#endif

// Un mensaje decodificado. VB.NET debe declarar una Structure con los
// mismos campos, en el mismo orden y con los mismos tamanos.
typedef struct
{
    float snr;      // SNR aproximado en dB
    float time_sec; // desfase de tiempo (DT) en segundos
    float freq_hz;  // frecuencia de audio en Hz
    int   score;    // puntaje de sincronizacion del candidato
    char  text[40]; // texto del mensaje, terminado en cero
} FT8_RESULT;

// Modos
#define FT8_MODO_FT8 0
#define FT8_MODO_FT4 1

// Devuelve la version de la DLL. Sirve para probar que VB.NET la encuentra.
FT8_API int FT8_Version(void);

// Decodifica un bloque de audio (normalmente 15 s, mono, valores entre -1 y 1).
// Devuelve cuantos mensajes escribio en "results", o un numero negativo si hubo error.
FT8_API int FT8_Decode(const float* samples, int num_samples, int sample_rate,
                       FT8_RESULT* results, int max_results);

// Convierte un texto ("CQ TI2XXX EK70") en audio FT8 listo para transmitir.
// Devuelve cuantas muestras escribio en "samples", o un numero negativo si hubo error:
//   -1 parametros invalidos, -2 buffer muy pequeno, -11..-15 texto no valido.
FT8_API int FT8_Encode(const char* text, float frequency_hz, int sample_rate,
                       float* samples, int max_samples);

// Igual que FT8_Decode / FT8_Encode, pero eligiendo el modo (FT8_MODO_FT8 o FT8_MODO_FT4).
// FT4: ciclos de 7.5 s, la senal dura 5.04 s.
FT8_API int FT8_DecodeEx(const float* samples, int num_samples, int sample_rate, int modo,
                         FT8_RESULT* results, int max_results);
FT8_API int FT8_EncodeEx(const char* text, float frequency_hz, int sample_rate, int modo,
                         float* samples, int max_samples);

// Decodificacion "a priori" (AP): mi indicativo, el de la estacion con la que estoy en QSO
// ("" si ninguna) y la frecuencia de audio donde la escucho (Hz). Llamar al cambiar de QSO.
FT8_API void FT8_SetAP(const char* micall, const char* dxcall, float frec_rx_hz);
