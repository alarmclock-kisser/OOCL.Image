// FIX: Helper-Funktionen vor Nutzung definieren (sonst 'use of undeclared identifier').
// Zusätzlich kleine Sicherungen & Kommentare.

inline float clamp255(float v)
{
    return fmin(fmax(v, 0.0f), 255.0f);
}

inline float luma(float3 c)
{
    return 0.299f * c.x + 0.587f * c.y + 0.114f * c.z;
}

__kernel void sharpen00(
    __global const uchar* inputPixels,
    __global uchar*       outputPixels,
    const int width,
    const int height,
    float amount,
    float threshold,
    const int passes)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    if (x >= width || y >= height) return;

    // Clamp Parameter
    amount    = fmax(0.0f, fmin(amount, 5.0f));
    threshold = fmax(0.0f, fmin(threshold, 1.0f));
    int p = passes;
    if (p < 1) p = 1;
    if (p > 4) p = 4;

    int idx = (y * width + x) * 4;

    // Original (immer unverändert aus input lesen)
    float3 orig = (float3)(
        (float)inputPixels[idx],
        (float)inputPixels[idx + 1],
        (float)inputPixels[idx + 2]);

    // Falls keine Schärfung gewünscht
    if (amount <= 0.0f) {
        outputPixels[idx]     = (uchar)orig.x;
        outputPixels[idx + 1] = (uchar)orig.y;
        outputPixels[idx + 2] = (uchar)orig.z;
        outputPixels[idx + 3] = inputPixels[idx + 3];
        return;
    }

    // Koordinaten für Nachbarn (geclamped)
    int xm1 = x > 0 ? x - 1 : 0;
    int xp1 = x < width  - 1 ? x + 1 : width  - 1;
    int ym1 = y > 0 ? y - 1 : 0;
    int yp1 = y < height - 1 ? y + 1 : height - 1;

    int iL = (y  * width + xm1) * 4;
    int iR = (y  * width + xp1) * 4;
    int iU = (ym1 * width + x)  * 4;
    int iD = (yp1 * width + x)  * 4;

    float3 left  = (float3)((float)inputPixels[iL], (float)inputPixels[iL + 1], (float)inputPixels[iL + 2]);
    float3 right = (float3)((float)inputPixels[iR], (float)inputPixels[iR + 1], (float)inputPixels[iR + 2]);
    float3 up    = (float3)((float)inputPixels[iU], (float)inputPixels[iU + 1], (float)inputPixels[iU + 2]);
    float3 down  = (float3)((float)inputPixels[iD], (float)inputPixels[iD + 1], (float)inputPixels[iD + 2]);

    // Einfacher Blur (gewichtetes Kreuz; Gesamtgewicht = 8)
    //  center *4  + (L+R+U+D)
    float3 blur = (4.0f * orig + left + right + up + down) * (1.0f / 8.0f);

    // Highpass
    float3 high = orig - blur;

    float thrAbs = threshold * 255.0f;
    float3 accum = orig;

    for (int pass = 0; pass < p; pass++) {
        float hl = fabs(luma(high));
        if (hl >= thrAbs) {
            // Schärfung addieren
            accum += amount * high;
            // Optional: erneute leichte Dämpfung (gegen Übersättigung)
            accum.x = clamp255(accum.x);
            accum.y = clamp255(accum.y);
            accum.z = clamp255(accum.z);
        }
        // Für einfache Iteration high neu aus accum ableiten (erneuter Blur)
        if (pass < p - 1) {
            float3 blur2 = (4.0f * accum + left + right + up + down) * (1.0f / 8.0f);
            high = accum - blur2;
        }
    }

    // Schreiben
    outputPixels[idx]     = (uchar)accum.x;
    outputPixels[idx + 1] = (uchar)accum.y;
    outputPixels[idx + 2] = (uchar)accum.z;
    outputPixels[idx + 3] = inputPixels[idx + 3]; // Alpha unverändert
}