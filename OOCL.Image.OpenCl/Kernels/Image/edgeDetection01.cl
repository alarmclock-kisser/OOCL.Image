__kernel void edgeDetection01(
    __global const uchar* inputPixels,
    __global uchar* outputPixels,
    const int width,
    const int height,
    float threshold,
    const int thickness,
    const int edgeR,
    const int edgeG,
    const int edgeB)
{
    int x = get_global_id(0);
    int y = get_global_id(1);

    if (x >= width || y >= height) return;

    const int pixelPos = (y * width + x) * 4;

    // Ausgangsbild zuerst 1:1 (inkl. R/B-Tausch wie bisher) ins Output kopieren
    // Annahme: inputPixels Layout wie im vorherigen Kernel genutzt (R,B vertauscht gewollt)
    outputPixels[pixelPos]     = inputPixels[pixelPos];     // R
    outputPixels[pixelPos + 1] = inputPixels[pixelPos + 1]; // G
    outputPixels[pixelPos + 2] = inputPixels[pixelPos + 2]; // B
    outputPixels[pixelPos + 3] = inputPixels[pixelPos + 3]; // A

    // Parameter clampen
    const uchar clampedB = (uchar)min(max(edgeR, 0), 255);  // R-Eingabe -> B-Ausgabe (Swap beibehalten)
    const uchar clampedG = (uchar)min(max(edgeG, 0), 255);
    const uchar clampedR = (uchar)min(max(edgeB, 0), 255);  // B-Eingabe -> R-Ausgabe
    const int   clampedThickness = min(max(thickness, 0), 10);
    const float absThreshold = fabs(threshold);

    // Nur Nicht-Randpixel für Sobel berechnen
    if (x < clampedThickness || x >= width - clampedThickness ||
        y < clampedThickness || y >= height - clampedThickness)
    {
        return;
    }

    // Sobel-Filter
    const int sobelX[3][3] = { {-1, 0, 1}, {-2, 0, 2}, {-1, 0, 1} };
    const int sobelY[3][3] = { {-1,-2,-1}, { 0, 0, 0}, { 1, 2, 1} };

    float3 gx = (float3)(0.0f);
    float3 gy = (float3)(0.0f);

    for (int dy = -1; dy <= 1; dy++)
    {
        int ny = y + dy;
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx;
            int neighborPos = (ny * width + nx) * 4;

            // R/B vertauscht wie im Referenzkernel (für Konsistenz)
            float3 rgb = (float3)(
                inputPixels[neighborPos + 2] / 255.0f, // B -> R
                inputPixels[neighborPos + 1] / 255.0f, // G
                inputPixels[neighborPos]     / 255.0f  // R -> B
            );

            int kx = sobelX[dy + 1][dx + 1];
            int ky = sobelY[dy + 1][dx + 1];

            gx += rgb * (float)kx;
            gy += rgb * (float)ky;
        }
    }

    float3 magnitude = sqrt(gx * gx + gy * gy);
    float avgMagnitude = (magnitude.x + magnitude.y + magnitude.z) / 3.0f;

    if (avgMagnitude > absThreshold)
    {
        // Kantenfarbe in Radius (Disk) schreiben
        for (int dy = -clampedThickness; dy <= clampedThickness; dy++)
        {
            int py = y + dy;
            if (py < 0 || py >= height) continue;

            for (int dx = -clampedThickness; dx <= clampedThickness; dx++)
            {
                if (dx*dx + dy*dy > clampedThickness * clampedThickness) continue;

                int px = x + dx;
                if (px < 0 || px >= width) continue;

                int outPos = (py * width + px) * 4;

                // Direkte Überzeichnung (kein Alpha-Blending)
                outputPixels[outPos]     = clampedR;  // (Swap beibehalten)
                outputPixels[outPos + 1] = clampedG;
                outputPixels[outPos + 2] = clampedB;
                outputPixels[outPos + 3] = 255;
            }
        }
    }
}