/* Loopback test: encode a JTTY message, add noise, decode it again. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>

int  jtty_pako_version(void);
void jtty_pako_reset(void);
int  jtty_pako_feed(const int16_t *s, int n, int nfa, int nfb, float f0, float ftol);
int  jtty_pako_get_updates(char *text, int64_t *ids, float *freq, float *tstart, int *complete);
int  jtty_pako_encode(const char *msg, int len, float f0, float *wave, int maxwave, char *canon);

static double gauss(void) {
  double u1 = (rand() + 1.0) / (RAND_MAX + 2.0), u2 = (rand() + 1.0) / (RAND_MAX + 2.0);
  return sqrt(-2 * log(u1)) * cos(2 * M_PI * u2);
}

int main(int argc, char **argv) {
  const char *msg = argc > 1 ? argv[1] : "CQ TI2LX CQ";
  double snr_db = argc > 2 ? atof(argv[2]) : -10.0;   /* SNR in 2500 Hz */
  float f0 = 1200.0f;
  static float wave[400000];
  char canon[81] = {0};

  printf("API version %d\n", jtty_pako_version());
  int nw = jtty_pako_encode(msg, (int)strlen(msg), f0, wave, 400000, canon);
  printf("Tx '%s' -> sent as '%.*s' : %d samples (%.2f s)\n", msg, 80, canon, nw, nw / 12000.0);
  if (nw <= 0) return 1;

  /* 2 s silence + signal + 3 s silence, plus white noise */
  int lead = 24000, tail = 36000, n = lead + nw + tail;
  int16_t *rx = calloc(n, sizeof *rx);
  double sig_rms = 1 / sqrt(2.0);
  double noise_rms = sig_rms / sqrt(pow(10, snr_db / 10) * 12000.0 / 2 / 2500.0);
  double scale = 1000.0 / noise_rms;
  if (scale * sig_rms > 8000) scale = 8000 / sig_rms;
  srand(1);
  for (int i = 0; i < n; i++) {
    double s = (i >= lead && i < lead + nw) ? wave[i - lead] : 0;
    double v = scale * (s + noise_rms * gauss());
    rx[i] = (int16_t)fmax(-32767, fmin(32767, v));
  }

  jtty_pako_reset();
  char text[30 * 80]; int64_t ids[30]; float fr[30], ts[30]; int cp[30];
  for (int k = 0; k < n; k += 1200) {                 /* 100 ms chunks */
    int m = (n - k < 1200) ? n - k : 1200;
    jtty_pako_feed(rx + k, m, 200, 3000, f0, 50);
    int c;
    do {
      c = jtty_pako_get_updates(text, ids, fr, ts, cp);
      for (int i = 0; i < c; i++)
        printf("  t=%5.1fs id=%lld f=%6.1f %s '%.*s'\n", (k + m) / 12000.0,
               (long long)ids[i], fr[i], cp[i] ? "[EOM]" : "     ", 80, text + i * 80);
    } while (c == 30);
  }
  free(rx);
  return 0;
}
