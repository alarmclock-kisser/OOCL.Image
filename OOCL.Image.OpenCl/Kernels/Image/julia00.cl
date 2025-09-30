#pragma OPENCL EXTENSION cl_khr_fp64 : enable

__kernel void julia00(
    __global uchar* outputPixels,
    int width,
    int height,
    double zoom,
    double offsetX,
    double offsetY,
    int iterCoeff,
    int baseR,
    int baseG,
    int baseB)
{
    int px = get_global_id(0);
    int py = get_global_id(1);

    if (px >= width || py >= height) return;

    // Clamp iterCoeff to range 1–1000
    iterCoeff = max(1, min(iterCoeff, 1000));

    // Dynamic iteration count based on zoom (same logic as mandelbrot00 for consistency)
    int maxIter = 100 + (int)(iterCoeff * log(zoom + 1.0));

    // Initial complex value z (centered, scaled by zoom)
    double zx = ((double)px - width / 2.0) / (width / 2.0) / zoom;
    double zy = ((double)py - height / 2.0) / (height / 2.0) / zoom;

    // Julia constant c taken from offset parameters so the kernel is callable with the same arguments.
    // (offsetX/offsetY are repurposed here as the complex constant c = offsetX + i*offsetY)
    double cX = offsetX;
    double cY = offsetY;

    int iter = 0;
    while (zx*zx + zy*zy <= 4.0 && iter < maxIter)
    {
        double xTemp = zx*zx - zy*zy + cX;
        zy = 2.0 * zx * zy + cY;
        zx = xTemp;
        iter++;
    }

    int idx = (py * width + px) * 4;

    if (iter == maxIter)
    {
        outputPixels[idx + 0] = baseR;
        outputPixels[idx + 1] = baseG;
        outputPixels[idx + 2] = baseB;
    }
    else
    {
        float t = (float)iter / (float)maxIter;
        float r = sin(t * 3.14159f) * 255.0f;
        float g = sin(t * 6.28318f + 1.0472f) * 255.0f;
        float b = sin(t * 9.42477f + 2.0944f) * 255.0f;

        outputPixels[idx + 0] = clamp((int)(baseR + r * (1.0f - t)), 0, 255);
        outputPixels[idx + 1] = clamp((int)(baseG + g * (1.0f - t)), 0, 255);
        outputPixels[idx + 2] = clamp((int)(baseB + b * (1.0f - t)), 0, 255);
    }

    outputPixels[idx + 3] = 255;
}
