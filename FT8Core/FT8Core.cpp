// FT8Core.cpp : puente entre ft8_lib y VB.NET
// Basado en demo/decode_ft8.c y demo/gen_ft8.c de ft8_lib (kgoba)

#include "FT8Core.h"

#include <cmath>
#include <cstring>
#include <cstdio>
#include <cstdint>
#include <vector>
#include <algorithm>

#include <ft8/decode.h>
#include <ft8/encode.h>
#include <ft8/message.h>
#include <ft8/constants.h>
#include <ft8/ldpc.h>
#include <ft8/crc.h>
#include <common/monitor.h>

// ---------------------------------------------------------------------------
// Parametros del decodificador (los mismos del ejemplo decode_ft8.c)
// ---------------------------------------------------------------------------
#ifndef FT8_TEST
static const
#endif
int kMinScore = 10;                    // puntaje minimo para considerar un candidato
#ifndef FT8_TEST
static const
#endif
int kMaxCandidates = 140;              // candidatos maximos por pasada
#ifndef FT8_TEST
static const
#endif
int kLdpcIterations = 25;              // iteraciones del decodificador LDPC
#ifndef FT8_TEST
static const
#endif
int kPasadas = 3;                      // pasadas de decodificacion (con resta entre pasadas)
#ifndef FT8_TEST
static const
#endif
float kSnrOffset = -26.2f;             // calibracion del SNR a la escala de WSJT-X (2500 Hz)
#ifndef FT8_TEST
static const
#endif
float kSnrExtraFT4 = 5.2f;             // FT4: 10*log10(20.83 / 6.25) por los bins mas anchos
static const int kMaxDecoded = 100;    // mensajes maximos por ciclo
static const int kFreqOsr = 2;         // sobremuestreo en frecuencia
static const int kTimeOsr = 2;         // sobremuestreo en tiempo

static const float kPi = 3.14159265358979323846f;

// ---------------------------------------------------------------------------
// Tabla de indicativos "hash" (para mensajes con indicativos no estandar, <...>)
// Se conserva entre llamadas, igual que en WSJT-X.
// ---------------------------------------------------------------------------
#define CALLSIGN_HASHTABLE_SIZE 256

static struct
{
    char callsign[12];
    uint32_t hash; // 8 bits altos = edad; 22 bits bajos = hash
} g_hashtable[CALLSIGN_HASHTABLE_SIZE];

static int g_hashtable_size = 0;

static void hashtable_cleanup(uint8_t max_age)
{
    for (int i = 0; i < CALLSIGN_HASHTABLE_SIZE; ++i)
    {
        if (g_hashtable[i].callsign[0] != '\0')
        {
            uint8_t age = (uint8_t)(g_hashtable[i].hash >> 24);
            if (age > max_age)
            {
                g_hashtable[i].callsign[0] = '\0';
                g_hashtable[i].hash = 0;
                g_hashtable_size--;
            }
            else
            {
                g_hashtable[i].hash = (((uint32_t)age + 1u) << 24) | (g_hashtable[i].hash & 0x3FFFFFu);
            }
        }
    }
}

static void hashtable_add(const char* callsign, uint32_t hash)
{
    uint16_t hash10 = (hash >> 12) & 0x3FFu;
    int idx = (hash10 * 23) % CALLSIGN_HASHTABLE_SIZE;
    while (g_hashtable[idx].callsign[0] != '\0')
    {
        if (((g_hashtable[idx].hash & 0x3FFFFFu) == hash) && (0 == strcmp(g_hashtable[idx].callsign, callsign)))
        {
            g_hashtable[idx].hash &= 0x3FFFFFu; // reinicia la edad
            return;
        }
        idx = (idx + 1) % CALLSIGN_HASHTABLE_SIZE;
    }
    if (g_hashtable_size >= CALLSIGN_HASHTABLE_SIZE - 1)
        return; // tabla llena
    g_hashtable_size++;
    strncpy(g_hashtable[idx].callsign, callsign, 11);
    g_hashtable[idx].callsign[11] = '\0';
    g_hashtable[idx].hash = hash;
}

static bool hashtable_lookup(ftx_callsign_hash_type_t hash_type, uint32_t hash, char* callsign)
{
    uint8_t hash_shift = (hash_type == FTX_CALLSIGN_HASH_10_BITS) ? 12 : (hash_type == FTX_CALLSIGN_HASH_12_BITS ? 10 : 0);
    uint16_t hash10 = (hash >> (12 - hash_shift)) & 0x3FFu;
    int idx = (hash10 * 23) % CALLSIGN_HASHTABLE_SIZE;
    while (g_hashtable[idx].callsign[0] != '\0')
    {
        if (((g_hashtable[idx].hash & 0x3FFFFFu) >> hash_shift) == hash)
        {
            strcpy(callsign, g_hashtable[idx].callsign);
            return true;
        }
        idx = (idx + 1) % CALLSIGN_HASHTABLE_SIZE;
    }
    callsign[0] = '\0';
    return false;
}

static ftx_callsign_hash_interface_t g_hash_if = { hashtable_lookup, hashtable_add };

// ---------------------------------------------------------------------------
// Mensajes "modo DXpedicion" (tipo 0.1), que ft8_lib no decodifica:
//   "K1ABC RR73; W9XYZ <KH1/KH7Z> -12"
// La estacion DX cierra un QSO (RR73 a K1ABC) y le da reporte a otra (W9XYZ)
// en un solo mensaje. Bits: c28 c28 h10 r5 n3=1 i3=0
// ---------------------------------------------------------------------------
static uint32_t leer_bits(const uint8_t* p, int inicio, int n)
{
    uint32_t v = 0;
    for (int i = 0; i < n; ++i)
    {
        int b = inicio + i;
        v = (v << 1) | ((p[b / 8] >> (7 - b % 8)) & 1u);
    }
    return v;
}

static void poner_bits(uint8_t* p, int inicio, int n, uint32_t v)
{
    for (int i = 0; i < n; ++i)
    {
        int b = inicio + i;
        uint8_t bit = (uint8_t)((v >> (n - 1 - i)) & 1u);
        if (bit) p[b / 8] |= (uint8_t)(0x80u >> (b % 8));
        else     p[b / 8] &= (uint8_t)~(0x80u >> (b % 8));
    }
}

static bool decodificar_dxpedicion(const ftx_message_t* msg, char* texto, size_t tam)
{
    if (ftx_message_get_type(msg) != FTX_MESSAGE_TYPE_DXPEDITION)
        return false;

    uint32_t c28a = leer_bits(msg->payload, 0, 28);
    uint32_t c28b = leer_bits(msg->payload, 28, 28);
    uint32_t h10 = leer_bits(msg->payload, 56, 10);
    uint32_t r5 = leer_bits(msg->payload, 66, 5);

    // Los dos indicativos: armar un mensaje estandar "CALL1 CALL2" y que ft8_lib lo convierta
    ftx_message_t std;
    ftx_message_init(&std);
    memset(std.payload, 0, sizeof(std.payload));
    poner_bits(std.payload, 0, 29, c28a << 1);
    poner_bits(std.payload, 29, 29, c28b << 1);
    poner_bits(std.payload, 58, 1, 0);
    poner_bits(std.payload, 59, 15, 32400u + 1u);   // sin grid ni reporte
    poner_bits(std.payload, 74, 3, 1);              // i3 = 1 (estandar)

    char dos[FTX_MAX_MESSAGE_LENGTH];
    ftx_message_offsets_t offs;
    if (ftx_message_decode(&std, &g_hash_if, dos, &offs) != FTX_MESSAGE_RC_OK)
        return false;
    char call1[16] = "", call2[16] = "";
    if (sscanf(dos, "%15s %15s", call1, call2) != 2)
        return false;

    // Indicativo de la estacion DX: hash de 10 bits (lo conocemos si ya aparecio antes)
    char dx[20];
    char buscado[16];
    if (hashtable_lookup(FTX_CALLSIGN_HASH_10_BITS, h10, buscado))
        snprintf(dx, sizeof(dx), "<%s>", buscado);
    else
        strcpy(dx, "<...>");

    int reporte = 2 * (int)r5 - 30;
    snprintf(texto, tam, "%s RR73; %s %s %+03d", call1, call2, dx, reporte);
    return true;
}

// ---------------------------------------------------------------------------
// Version
// ---------------------------------------------------------------------------
FT8_API int FT8_Version(void)
{
    return 4; // 4 = FT8, FT4, DXpedicion, OSD y AP
}

// ---------------------------------------------------------------------------
// Decodificar (varias pasadas con resta de senales + SNR real)
// ---------------------------------------------------------------------------

// Posicion en la cascada: "fila" = medio simbolo (0.08 s), "fbin" = 1/2 bin (3.125 Hz)
static inline int wf_index(const ftx_waterfall_t* wf, int fila, int fbin)
{
    int block = fila / wf->time_osr;
    int tsub = fila % wf->time_osr;
    int bin = fbin / wf->freq_osr;
    int fsub = fbin % wf->freq_osr;
    return ((block * wf->time_osr + tsub) * wf->freq_osr + fsub) * wf->num_bins + bin;
}

static inline bool wf_inside(const ftx_waterfall_t* wf, int fila, int fbin)
{
    return fila >= 0 && fila < wf->num_blocks * wf->time_osr &&
           fbin >= 0 && fbin < wf->num_bins * wf->freq_osr;
}

// Piso de ruido por columna de frecuencia.
// 1) mediana en el tiempo de cada columna; 2) como las senales fuertes "ensucian" sus propias
// columnas, el piso final es el percentil 20 de esas medianas en +/- 400 Hz alrededor.
static void calcular_piso_ruido(const ftx_waterfall_t* wf, float periodo_simbolo, std::vector<uint8_t>& piso)
{
    int filas = wf->num_blocks * wf->time_osr;
    int fbins = wf->num_bins * wf->freq_osr;
    std::vector<uint8_t> mediana(fbins, 0);
    std::vector<uint8_t> col(filas);
    for (int f = 0; f < fbins; ++f)
    {
        for (int t = 0; t < filas; ++t)
            col[t] = wf->mag[wf_index(wf, t, f)];
        std::nth_element(col.begin(), col.begin() + filas / 2, col.end());
        mediana[f] = col[filas / 2];
    }

    const int ventana = (int)(400.0f * periodo_simbolo * wf->freq_osr); // 400 Hz en fbins
    piso.assign(fbins, 0);
    std::vector<uint8_t> vecinos;
    for (int f = 0; f < fbins; ++f)
    {
        int a = (std::max)(0, f - ventana), b = (std::min)(fbins, f + ventana + 1);
        vecinos.assign(mediana.begin() + a, mediana.begin() + b);
        size_t k = vecinos.size() / 5;
        std::nth_element(vecinos.begin(), vecinos.begin() + k, vecinos.end());
        piso[f] = vecinos[k];
    }
}

// Recorre las celdas (fila, fbin) donde esta la energia de una senal ya decodificada.
// La fila 2i del candidato corresponde al simbolo i; la fila 2i+1 mezcla los simbolos i e i+1.
template <typename F>
static void recorrer_senal(const ftx_waterfall_t* wf, const ftx_candidate_t* cand,
                           const uint8_t* tones, int nn, F accion)
{
    int fila0 = cand->time_offset * wf->time_osr + cand->time_sub;
    int fbin0 = cand->freq_offset * wf->freq_osr + cand->freq_sub;
    for (int i = 0; i < nn; ++i)
    {
        for (int d = -1; d <= 1; ++d) // fila anterior, la del simbolo y la siguiente
        {
            int fila = fila0 + 2 * i + d;
            for (int w = -1; w <= 1; ++w) // +/- 1 fbin alrededor del tono
            {
                accion(fila, fbin0 + tones[i] * wf->freq_osr + w);
            }
        }
    }
}

// "Borra" una senal de la cascada: pone sus celdas al nivel del ruido
static void restar_senal(ftx_waterfall_t* wf, const ftx_candidate_t* cand,
                         const uint8_t* tones, int nn, const std::vector<uint8_t>& piso)
{
    recorrer_senal(wf, cand, tones, nn, [&](int fila, int fbin) {
        if (!wf_inside(wf, fila, fbin))
            return;
        uint8_t& m = wf->mag[wf_index(wf, fila, fbin)];
        if (m > piso[fbin])
            m = piso[fbin];
    });
}

// SNR estilo WSJT-X (ruido medido en 2500 Hz de ancho de banda)
static float calcular_snr(const ftx_waterfall_t* wf, const ftx_candidate_t* cand,
                          const uint8_t* tones, int nn, float offset_snr, const std::vector<uint8_t>& piso)
{
    int fila0 = cand->time_offset * wf->time_osr + cand->time_sub;
    int fbin0 = cand->freq_offset * wf->freq_osr + cand->freq_sub;
    double sum_sig = 0, sum_ruido = 0;
    int n = 0;
    for (int i = 0; i < nn; ++i)
    {
        int fila = fila0 + 2 * i;
        int fbin = fbin0 + tones[i] * wf->freq_osr;
        if (!wf_inside(wf, fila, fbin))
            continue;
        double db_sig = wf->mag[wf_index(wf, fila, fbin)] * 0.5 - 120.0;
        double db_ruido = piso[fbin] * 0.5 - 120.0 + 1.59; // mediana -> promedio (ruido exponencial)
        sum_sig += std::pow(10.0, db_sig / 10.0);
        sum_ruido += std::pow(10.0, db_ruido / 10.0);
        ++n;
    }
    if (n == 0 || sum_ruido <= 0)
        return -30.0f;
    double relacion = sum_sig / sum_ruido - 1.0; // quitar el ruido que cae dentro del tono
    if (relacion < 1e-3)
        relacion = 1e-3;
    double snr = 10.0 * std::log10(relacion) + offset_snr;
    if (snr < -30)
        snr = -30;
    return (float)snr;
}

// ===========================================================================
// DECODIFICACION PROFUNDA: OSD y AP ("a priori")
//
// ft8_lib solo usa BP (propagacion de creencias) para el LDPC. Cuando BP no
// converge, se prueba:
//   OSD: "ordered statistics decoding". Se toman los 91 bits mas confiables,
//        se rearma el codigo completo a partir de ellos y se prueban variantes
//        cambiando 1 bit (orden 1). Se acepta si pasa el CRC y la distancia a
//        lo recibido es chica.
//   AP:  si ya sabemos parte del mensaje (nuestro indicativo, el del DX, CQ),
//        esos bits se fijan antes de decodificar. Rinde varios dB con la
//        estacion con la que estamos haciendo el QSO.
// ===========================================================================
#ifndef FT8_TEST
static
#endif
bool kUsarOsd = true;
#ifndef FT8_TEST
static
#endif
bool kUsarAp = true;
#ifndef FT8_TEST
static
#endif
int kOsdMaxErrores = 32;        // bits duros distintos (de 174) para aceptar una decodificacion OSD / AP
#ifndef FT8_TEST
static
#endif
float kApAnchoHz = 100.0f;      // AP con el indicativo del DX: solo cerca de la frecuencia de RX

struct Bits174
{
    uint64_t w[3];
};
static inline int bit_de(const Bits174& b, int i) { return (int)((b.w[i >> 6] >> (i & 63)) & 1u); }
static inline void poner_bit(Bits174& b, int i) { b.w[i >> 6] |= (uint64_t)1 << (i & 63); }
static inline void xor_bits(Bits174& a, const Bits174& b)
{
    a.w[0] ^= b.w[0];
    a.w[1] ^= b.w[1];
    a.w[2] ^= b.w[2];
}

// Matriz generadora completa: fila i = palabra de codigo del mensaje con solo el bit i en 1
static Bits174 g_gen[FTX_LDPC_K];
static bool g_gen_lista = false;

static void preparar_generadora()
{
    if (g_gen_lista)
        return;
    for (int i = 0; i < FTX_LDPC_K; ++i)
    {
        Bits174 f = { { 0, 0, 0 } };
        poner_bit(f, i); // parte sistematica
        for (int j = 0; j < FTX_LDPC_M; ++j)
            if (kFTX_LDPC_generator[j][i / 8] & (0x80u >> (i % 8)))
                poner_bit(f, FTX_LDPC_K + j);
        g_gen[i] = f;
    }
    g_gen_lista = true;
}

// LLR de los 174 bits del candidato (copia de ft8_lib/decode.c, que las deja privadas)
static float max2f(float a, float b) { return (a >= b) ? a : b; }
static float max4f(float a, float b, float c, float d) { return max2f(max2f(a, b), max2f(c, d)); }
static inline float mag_db(uint8_t x) { return (float)x * 0.5f - 120.0f; }

static void extraer_llr(const ftx_waterfall_t* wf, const ftx_candidate_t* cand, bool ft4, float* llr)
{
    int offset = cand->time_offset;
    offset = (offset * wf->time_osr) + cand->time_sub;
    offset = (offset * wf->freq_osr) + cand->freq_sub;
    offset = (offset * wf->num_bins) + cand->freq_offset;
    const uint8_t* mag = wf->mag + offset;

    if (ft4)
    {
        for (int k = 0; k < FT4_ND; ++k)
        {
            int sym = k + ((k < 29) ? 5 : ((k < 58) ? 9 : 13));
            int b = 2 * k;
            int block = cand->time_offset + sym;
            if (block < 0 || block >= wf->num_blocks)
            {
                llr[b] = llr[b + 1] = 0;
                continue;
            }
            const uint8_t* p = mag + sym * wf->block_stride;
            float s[4];
            for (int j = 0; j < 4; ++j)
                s[j] = mag_db(p[kFT4_Gray_map[j]]);
            llr[b + 0] = max2f(s[2], s[3]) - max2f(s[0], s[1]);
            llr[b + 1] = max2f(s[1], s[3]) - max2f(s[0], s[2]);
        }
    }
    else
    {
        for (int k = 0; k < FT8_ND; ++k)
        {
            int sym = k + ((k < 29) ? 7 : 14);
            int b = 3 * k;
            int block = cand->time_offset + sym;
            if (block < 0 || block >= wf->num_blocks)
            {
                llr[b] = llr[b + 1] = llr[b + 2] = 0;
                continue;
            }
            const uint8_t* p = mag + sym * wf->block_stride;
            float s[8];
            for (int j = 0; j < 8; ++j)
                s[j] = mag_db(p[kFT8_Gray_map[j]]);
            llr[b + 0] = max4f(s[4], s[5], s[6], s[7]) - max4f(s[0], s[1], s[2], s[3]);
            llr[b + 1] = max4f(s[2], s[3], s[6], s[7]) - max4f(s[0], s[1], s[4], s[5]);
            llr[b + 2] = max4f(s[1], s[3], s[5], s[7]) - max4f(s[0], s[2], s[4], s[6]);
        }
    }

    // Normalizar (igual que ft8_lib)
    float sum = 0, sum2 = 0;
    for (int i = 0; i < FTX_LDPC_N; ++i)
    {
        sum += llr[i];
        sum2 += llr[i] * llr[i];
    }
    float inv_n = 1.0f / FTX_LDPC_N;
    float var = (sum2 - sum * sum * inv_n) * inv_n;
    float factor = (var > 0) ? sqrtf(24.0f / var) : 1.0f;
    for (int i = 0; i < FTX_LDPC_N; ++i)
        llr[i] *= factor;
}

// Palabra de 174 bits (0/1) -> mensaje, si pasa el CRC
static bool palabra_a_mensaje(const uint8_t* plain, bool ft4, ftx_message_t* msg)
{
    uint8_t a91[FTX_LDPC_K_BYTES];
    memset(a91, 0, sizeof(a91));
    for (int i = 0; i < FTX_LDPC_K; ++i)
        if (plain[i])
            a91[i / 8] |= (uint8_t)(0x80u >> (i % 8));

    uint16_t crc_leido = ftx_extract_crc(a91);
    a91[9] &= 0xF8;
    a91[10] &= 0x00;
    if (crc_leido != ftx_compute_crc(a91, 96 - 14))
        return false;

    ftx_message_init(msg);
    msg->hash = crc_leido;
    for (int i = 0; i < 10; ++i)
        msg->payload[i] = ft4 ? (uint8_t)(a91[i] ^ kFT4_XOR_sequence[i]) : a91[i];
    return true;
}

// Bits distintos entre una palabra (0/1) y las decisiones duras de las LLR
static int errores_duros(const uint8_t* plain, const float* llr)
{
    int n = 0;
    for (int i = 0; i < FTX_LDPC_N; ++i)
        if ((llr[i] > 0) != (plain[i] != 0))
            ++n;
    return n;
}

// OSD de orden 1 (y orden 2 sobre los 12 bits menos confiables del conjunto de informacion)
static bool decodificar_osd(const float* llr, uint8_t* plain_out)
{
    preparar_generadora();

    // Ordenar las posiciones de mas a menos confiable
    int orden[FTX_LDPC_N];
    for (int i = 0; i < FTX_LDPC_N; ++i)
        orden[i] = i;
    std::sort(orden, orden + FTX_LDPC_N, [&](int a, int b) { return fabsf(llr[a]) > fabsf(llr[b]); });

    // Generadora con las columnas en ese orden
    Bits174 filas[FTX_LDPC_K];
    for (int i = 0; i < FTX_LDPC_K; ++i)
    {
        Bits174 f = { { 0, 0, 0 } };
        for (int k = 0; k < FTX_LDPC_N; ++k)
            if (bit_de(g_gen[i], orden[k]))
                poner_bit(f, k);
        filas[i] = f;
    }

    // Eliminacion gaussiana: identidad en las primeras 91 columnas independientes
    int pivote_col[FTX_LDPC_K];
    int piv = 0;
    for (int k = 0; k < FTX_LDPC_N && piv < FTX_LDPC_K; ++k)
    {
        int r = -1;
        for (int i = piv; i < FTX_LDPC_K; ++i)
            if (bit_de(filas[i], k))
            {
                r = i;
                break;
            }
        if (r < 0)
            continue;
        std::swap(filas[piv], filas[r]);
        for (int i = 0; i < FTX_LDPC_K; ++i)
            if (i != piv && bit_de(filas[i], k))
                xor_bits(filas[i], filas[piv]);
        pivote_col[piv] = k;
        ++piv;
    }
    if (piv < FTX_LDPC_K)
        return false;

    // Decisiones duras y confiabilidad, en el orden nuevo
    Bits174 duro = { { 0, 0, 0 } };
    float conf[FTX_LDPC_N];
    for (int k = 0; k < FTX_LDPC_N; ++k)
    {
        if (llr[orden[k]] > 0)
            poner_bit(duro, k);
        conf[k] = fabsf(llr[orden[k]]);
    }

    // Orden 0: rearmar desde los bits de informacion
    Bits174 c0 = { { 0, 0, 0 } };
    for (int i = 0; i < FTX_LDPC_K; ++i)
        if (bit_de(duro, pivote_col[i]))
            xor_bits(c0, filas[i]);

    auto distancia = [&](const Bits174& c) {
        Bits174 d = c;
        xor_bits(d, duro);
        float s = 0;
        for (int w = 0; w < 3; ++w)
        {
            uint64_t x = d.w[w];
            while (x)
            {
                int b = 0;
                uint64_t y = x & (~x + 1); // bit mas bajo
                while ((y >> b) != 1u)
                    ++b;
                s += conf[w * 64 + b];
                x &= x - 1;
            }
        }
        return s;
    };

    uint8_t plain[FTX_LDPC_N];
    ftx_message_t tmp;
    auto probar = [&](const Bits174& c, float& mejor, bool& hay) {
        float d = distancia(c);
        if (d >= mejor)
            return;
        for (int k = 0; k < FTX_LDPC_N; ++k)
            plain[orden[k]] = (uint8_t)bit_de(c, k);
        if (!palabra_a_mensaje(plain, false, &tmp)) // el CRC no depende del XOR de FT4
            return;
        mejor = d;
        hay = true;
        memcpy(plain_out, plain, FTX_LDPC_N);
    };

    float mejor = 1e30f;
    bool hay = false;
    probar(c0, mejor, hay);
    for (int i = 0; i < FTX_LDPC_K; ++i)
    {
        Bits174 c = c0;
        xor_bits(c, filas[i]);
        probar(c, mejor, hay);
    }
    // Orden 2 limitado: pares entre los 12 bits de informacion menos confiables
    const int kDos = 12;
    for (int a = FTX_LDPC_K - kDos; a < FTX_LDPC_K; ++a)
        for (int b = a + 1; b < FTX_LDPC_K; ++b)
        {
            Bits174 c = c0;
            xor_bits(c, filas[a]);
            xor_bits(c, filas[b]);
            probar(c, mejor, hay);
        }
    return hay;
}

// ---------------------------------------------------------------------------
// AP: patrones de bits conocidos
// ---------------------------------------------------------------------------
static char g_ap_micall[16] = "";
static char g_ap_dxcall[16] = "";
static float g_ap_frec = -1;

struct PatronAp
{
    int tipo;              // 1 CQ, 2 MICALL, 3 MICALL DX, 4-6 MICALL DX RRR/73/RR73
    bool solo_cerca;       // solo cerca de la frecuencia de RX
    int n;                 // bits conocidos
    uint8_t pos[77];
    uint8_t val[77];
};

// Arma un patron a partir de un mensaje de ejemplo, tomando los bits [0, hasta) y el i3
static bool armar_patron(const char* texto, int hasta, bool ft4, int tipo, bool cerca, PatronAp& p)
{
    ftx_message_t m;
    ftx_message_init(&m);
    if (ftx_message_encode(&m, NULL, texto) != FTX_MESSAGE_RC_OK)
        return false;
    if (ftx_message_get_i3(&m) != 1)
        return false; // solo mensajes estandar
    p.tipo = tipo;
    p.solo_cerca = cerca;
    p.n = 0;
    for (int i = 0; i < 77; ++i)
    {
        if (i >= hasta && i < 74)
            continue;
        int v = (m.payload[i / 8] >> (7 - i % 8)) & 1;
        if (ft4)
            v ^= (kFT4_XOR_sequence[i / 8] >> (7 - i % 8)) & 1;
        p.pos[p.n] = (uint8_t)i;
        p.val[p.n] = (uint8_t)v;
        ++p.n;
    }
    return true;
}

static void armar_patrones(bool ft4, std::vector<PatronAp>& patrones)
{
    patrones.clear();
    PatronAp p;
    char buf[64];
    if (armar_patron("CQ K1ABC FN42", 29, ft4, 1, false, p))
        patrones.push_back(p);
    if (g_ap_micall[0] == '\0')
        return;
    snprintf(buf, sizeof(buf), "%s K1ABC FN42", g_ap_micall);
    if (armar_patron(buf, 29, ft4, 2, false, p))
        patrones.push_back(p);
    if (g_ap_dxcall[0] == '\0')
        return;
    snprintf(buf, sizeof(buf), "%s %s FN42", g_ap_micall, g_ap_dxcall);
    if (armar_patron(buf, 58, ft4, 3, true, p))
        patrones.push_back(p);
    const char* finales[3] = { "RRR", "73", "RR73" };
    for (int k = 0; k < 3; ++k)
    {
        snprintf(buf, sizeof(buf), "%s %s %s", g_ap_micall, g_ap_dxcall, finales[k]);
        if (armar_patron(buf, 77, ft4, 4 + k, true, p))
            patrones.push_back(p);
    }
}

// Un mensaje "creible" para aceptarlo por OSD / AP: estandar o DXpedicion, sin indicativos desconocidos
static bool mensaje_creible(const ftx_message_t* msg)
{
    ftx_message_type_t t = ftx_message_get_type(msg);
    if (t != FTX_MESSAGE_TYPE_STANDARD && t != FTX_MESSAGE_TYPE_DXPEDITION)
        return false;
    char texto[FTX_MAX_MESSAGE_LENGTH];
    ftx_message_offsets_t offs;
    if (t == FTX_MESSAGE_TYPE_DXPEDITION)
    {
        // el indicativo del DX tiene que ser conocido (ya escuchado antes)
        if (!decodificar_dxpedicion(msg, texto, sizeof(texto)) || strstr(texto, "<...>") != NULL)
            return false;
    }
    if (t == FTX_MESSAGE_TYPE_STANDARD)
    {
        if (ftx_message_decode(msg, NULL, texto, &offs) != FTX_MESSAGE_RC_OK)
            return false;
        if (strstr(texto, "<") != NULL)
            return false;
    }
    return true;
}

// Decodifica un candidato: BP, luego OSD, luego AP. tipo: 0 BP, 1 OSD, 10+n AP tipo n
static bool decodificar_candidato(const ftx_waterfall_t* wf, const ftx_candidate_t* cand, bool ft4, float frec_hz,
                                  const std::vector<PatronAp>& patrones, ftx_message_t* msg, int* tipo)
{
    float llr[FTX_LDPC_N];
    extraer_llr(wf, cand, ft4, llr);

    float copia[FTX_LDPC_N];
    uint8_t plain[FTX_LDPC_N];
    int errores = 0;

    // 1) BP, como siempre
    memcpy(copia, llr, sizeof(llr));
    bp_decode(copia, kLdpcIterations, plain, &errores);
    if (errores == 0 && palabra_a_mensaje(plain, ft4, msg))
    {
        *tipo = 0;
        return true;
    }

    // 2) OSD sin informacion previa
    if (kUsarOsd && decodificar_osd(llr, plain) && errores_duros(plain, llr) <= kOsdMaxErrores &&
        palabra_a_mensaje(plain, ft4, msg) && mensaje_creible(msg))
    {
        *tipo = 1;
        return true;
    }

    // 3) AP: fijar los bits que ya conocemos
    if (!kUsarAp)
        return false;
    float maximo = 0;
    for (int i = 0; i < FTX_LDPC_N; ++i)
        maximo = (std::max)(maximo, fabsf(llr[i]));
    float ap_mag = maximo * 1.01f;

    for (size_t k = 0; k < patrones.size(); ++k)
    {
        const PatronAp& p = patrones[k];
        if (p.solo_cerca && (g_ap_frec < 0 || fabsf(frec_hz - g_ap_frec) > kApAnchoHz))
            continue;
        memcpy(copia, llr, sizeof(llr));
        for (int j = 0; j < p.n; ++j)
            copia[p.pos[j]] = p.val[j] ? ap_mag : -ap_mag;

        float copia2[FTX_LDPC_N];
        memcpy(copia2, copia, sizeof(copia));
        bp_decode(copia2, kLdpcIterations, plain, &errores);
        bool ok = (errores == 0);
        if (!ok && kUsarOsd)
            ok = decodificar_osd(copia, plain);
        if (!ok)
            continue;
        // La palabra debe respetar el patron y estar cerca de lo recibido (sin AP)
        bool respeta = true;
        for (int j = 0; j < p.n && respeta; ++j)
            respeta = (plain[p.pos[j]] == p.val[j]);
        if (!respeta || errores_duros(plain, llr) > kOsdMaxErrores)
            continue;
        if (palabra_a_mensaje(plain, ft4, msg) && mensaje_creible(msg))
        {
            *tipo = 10 + p.tipo;
            return true;
        }
    }
    return false;
}

// VB.NET avisa quien soy, con quien estoy en QSO y en que frecuencia lo escucho
FT8_API void FT8_SetAP(const char* micall, const char* dxcall, float frec_rx_hz)
{
    snprintf(g_ap_micall, sizeof(g_ap_micall), "%s", micall ? micall : "");
    snprintf(g_ap_dxcall, sizeof(g_ap_dxcall), "%s", dxcall ? dxcall : "");
    g_ap_frec = frec_rx_hz;
}

static int decodificar(const float* samples, int num_samples, int sample_rate, bool ft4,
                       FT8_RESULT* results, int max_results)
{
    if (samples == NULL || results == NULL || num_samples <= 0 || sample_rate <= 0 || max_results <= 0)
        return -1;

    // 1) Configurar el "monitor", que convierte el audio en una cascada (waterfall)
    monitor_config_t cfg;
    cfg.f_min = 200;
    cfg.f_max = 3000;
    cfg.sample_rate = sample_rate;
    cfg.time_osr = kTimeOsr;
    cfg.freq_osr = kFreqOsr;
    cfg.protocol = ft4 ? FTX_PROTOCOL_FT4 : FTX_PROTOCOL_FT8;

    monitor_t mon;
    monitor_init(&mon, &cfg);

    // 2) Pasar el audio bloque por bloque (un bloque = un simbolo FT8 = 0.16 s)
    for (int pos = 0; pos + mon.block_size <= num_samples; pos += mon.block_size)
    {
        monitor_process(&mon, samples + pos);
    }

    ftx_waterfall_t* wf = &mon.wf;
    std::vector<uint8_t> piso;
    calcular_piso_ruido(wf, mon.symbol_period, piso);
    const int nn = ft4 ? FT4_NN : FT8_NN;
    // FT4 usa bins 3.33 veces mas anchos (20.8 Hz vs 6.25 Hz): el ruido por bin es mayor
    const float offset_snr = kSnrOffset + (ft4 ? kSnrExtraFT4 : 0.0f);

    std::vector<ftx_message_t> decoded;
    decoded.reserve(kMaxDecoded);
    std::vector<ftx_candidate_t> candidates(kMaxCandidates);
    int num_results = 0;
    std::vector<PatronAp> patrones;
    armar_patrones(ft4, patrones);

    // 3) Varias pasadas: buscar, decodificar, restar lo decodificado y volver a buscar
    for (int pasada = 0; pasada < kPasadas && num_results < max_results; ++pasada)
    {
        int num_candidates = ftx_find_candidates(wf, kMaxCandidates, candidates.data(), kMinScore);

        // Primero decodificar todos los candidatos de esta pasada (sin tocar la cascada)
        struct Hallazgo { ftx_candidate_t cand; ftx_message_t msg; uint8_t tones[FT4_NN]; int tipo; }; // FT4_NN (105) > FT8_NN (79)
        std::vector<Hallazgo> nuevos;

        for (int i = 0; i < num_candidates && num_results + (int)nuevos.size() < max_results; ++i)
        {
            const ftx_candidate_t* cand = &candidates[i];

            ftx_message_t message;
            int tipo = 0;
            float frec = (mon.min_bin + cand->freq_offset + (float)cand->freq_sub / wf->freq_osr) / mon.symbol_period;
            if (!decodificar_candidato(wf, cand, ft4, frec, patrones, &message, &tipo))
                continue; // no paso LDPC o CRC

            // Evitar duplicados (el mismo mensaje en candidatos vecinos o en otra pasada)
            bool duplicate = false;
            for (size_t k = 0; k < decoded.size() && !duplicate; ++k)
                duplicate = (decoded[k].hash == message.hash &&
                             0 == memcmp(decoded[k].payload, message.payload, sizeof(message.payload)));
            if (duplicate || (int)decoded.size() >= kMaxDecoded)
                continue;
            decoded.push_back(message);

            Hallazgo h;
            h.cand = *cand;
            h.msg = message;
            h.tipo = tipo;
            if (ft4)
                ft4_encode(message.payload, h.tones);
            else
                ft8_encode(message.payload, h.tones);
            nuevos.push_back(h);
        }

        if (nuevos.empty())
            break; // nada nuevo: las siguientes pasadas no encontrarian mas

        for (size_t k = 0; k < nuevos.size(); ++k)
        {
            const Hallazgo& h = nuevos[k];

            // Convertir los 77 bits en texto
            char text[FTX_MAX_MESSAGE_LENGTH];
            ftx_message_offsets_t offsets;
            ftx_message_rc_t rc = ftx_message_decode(&h.msg, &g_hash_if, text, &offsets);
            if (rc != FTX_MESSAGE_RC_OK && !decodificar_dxpedicion(&h.msg, text, sizeof(text)))
                snprintf(text, sizeof(text), "Error [%d]", (int)rc);

            FT8_RESULT* r = &results[num_results++];
            r->freq_hz = (mon.min_bin + h.cand.freq_offset + (float)h.cand.freq_sub / wf->freq_osr) / mon.symbol_period;
            r->time_sec = (h.cand.time_offset + (float)h.cand.time_sub / wf->time_osr) * mon.symbol_period;
            r->score = h.cand.score + 1000 * h.tipo; // miles: como se decodifico (0 BP, 1 OSD, 1x AP tipo x)
            r->snr = calcular_snr(wf, &h.cand, h.tones, nn, offset_snr, piso);
            memset(r->text, 0, sizeof(r->text));
            strncpy(r->text, text, sizeof(r->text) - 1);
        }

        // Restar todas las senales encontradas antes de la siguiente pasada
        for (size_t k = 0; k < nuevos.size(); ++k)
            restar_senal(wf, &nuevos[k].cand, nuevos[k].tones, nn, piso);
    }

    hashtable_cleanup(10);
    monitor_free(&mon);
    return num_results;
}

FT8_API int FT8_Decode(const float* samples, int num_samples, int sample_rate,
                       FT8_RESULT* results, int max_results)
{
    return decodificar(samples, num_samples, sample_rate, false, results, max_results);
}

FT8_API int FT8_DecodeEx(const float* samples, int num_samples, int sample_rate, int modo,
                         FT8_RESULT* results, int max_results)
{
    return decodificar(samples, num_samples, sample_rate, modo == FT8_MODO_FT4, results, max_results);
}

// ---------------------------------------------------------------------------
// Codificar (sintesis GFSK, igual que gen_ft8.c)
// ---------------------------------------------------------------------------
static void gfsk_pulse(int n_spsym, float symbol_bt, float* pulse)
{
    const float K = 5.336446f; // pi * sqrt(2 / ln 2)
    for (int i = 0; i < 3 * n_spsym; ++i)
    {
        float t = i / (float)n_spsym - 1.5f;
        float arg1 = K * symbol_bt * (t + 0.5f);
        float arg2 = K * symbol_bt * (t - 0.5f);
        pulse[i] = (std::erf(arg1) - std::erf(arg2)) / 2;
    }
}

static void synth_gfsk(const uint8_t* symbols, int n_sym, float f0, float symbol_bt,
                       float symbol_period, int signal_rate, float* signal)
{
    int n_spsym = (int)(0.5f + signal_rate * symbol_period); // muestras por simbolo
    int n_wave = n_sym * n_spsym;
    float dphi_peak = 2 * kPi / n_spsym;

    std::vector<float> dphi(n_wave + 2 * n_spsym, 2 * kPi * f0 / signal_rate);
    std::vector<float> pulse(3 * n_spsym);
    gfsk_pulse(n_spsym, symbol_bt, pulse.data());

    for (int i = 0; i < n_sym; ++i)
    {
        int ib = i * n_spsym;
        for (int j = 0; j < 3 * n_spsym; ++j)
            dphi[j + ib] += dphi_peak * symbols[i] * pulse[j];
    }

    // Simbolos "fantasma" al inicio y al final para suavizar los bordes
    for (int j = 0; j < 2 * n_spsym; ++j)
    {
        dphi[j] += dphi_peak * pulse[j + n_spsym] * symbols[0];
        dphi[j + n_sym * n_spsym] += dphi_peak * pulse[j] * symbols[n_sym - 1];
    }

    float phi = 0;
    for (int k = 0; k < n_wave; ++k)
    {
        signal[k] = std::sin(phi);
        phi = std::fmod(phi + dphi[k + n_spsym], 2 * kPi);
    }

    // Rampa de subida y bajada (evita "clicks")
    int n_ramp = n_spsym / 8;
    for (int i = 0; i < n_ramp; ++i)
    {
        float env = (1 - std::cos(2 * kPi * i / (2 * n_ramp))) / 2;
        signal[i] *= env;
        signal[n_wave - 1 - i] *= env;
    }
}

static int codificar(const char* text, float frequency_hz, int sample_rate, bool ft4,
                     float* samples, int max_samples)
{
    if (text == NULL || samples == NULL || sample_rate <= 0 || frequency_hz <= 0)
        return -1;

    // 1) Texto -> 77 bits
    ftx_message_t msg;
    ftx_message_rc_t rc = ftx_message_encode(&msg, &g_hash_if, text);
    if (rc != FTX_MESSAGE_RC_OK)
        return -10 - (int)rc;

    // 2) 77 bits -> tonos (FT8: 79 tonos de 0..7; FT4: 105 tonos de 0..3)
    const int nn = ft4 ? FT4_NN : FT8_NN;
    const float periodo = ft4 ? FT4_SYMBOL_PERIOD : FT8_SYMBOL_PERIOD;
    const float bt = ft4 ? 1.0f : 2.0f; // suavizado GFSK
    uint8_t tones[FT4_NN];
    if (ft4)
        ft4_encode(msg.payload, tones);
    else
        ft8_encode(msg.payload, tones);

    // 3) Tonos -> audio (FT8: 12.64 s, FT4: 5.04 s)
    int n_spsym = (int)(0.5f + sample_rate * periodo);
    int num_samples = nn * n_spsym;
    if (max_samples < num_samples)
        return -2;

    synth_gfsk(tones, nn, frequency_hz, bt, periodo, sample_rate, samples);
    return num_samples;
}

FT8_API int FT8_Encode(const char* text, float frequency_hz, int sample_rate,
                       float* samples, int max_samples)
{
    return codificar(text, frequency_hz, sample_rate, false, samples, max_samples);
}

FT8_API int FT8_EncodeEx(const char* text, float frequency_hz, int sample_rate, int modo,
                         float* samples, int max_samples)
{
    return codificar(text, frequency_hz, sample_rate, modo == FT8_MODO_FT4, samples, max_samples);
}
