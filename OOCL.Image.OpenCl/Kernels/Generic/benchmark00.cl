#pragma OPENCL EXTENSION cl_khr_fp64 : enable
#pragma OPENCL EXTENSION cl_khr_int64_base_atomics : enable
#pragma OPENCL EXTENSION cl_khr_int64_extended_atomics : enable

// Atomarer Double-Add via 64-bit CAS (OpenCL)
static double atomicAddDouble(__global double* address, double val)
{
    volatile __global unsigned long* addr_as_ul = (volatile __global unsigned long*)address;
    unsigned long old = *addr_as_ul;
    unsigned long assumed;
    do
    {
        assumed = old;
        double oldVal = as_double(assumed);
        double newVal = oldVal + val;
        unsigned long newValAsUl = as_ulong(newVal);
        old = atomic_cmpxchg(addr_as_ul, assumed, newValAsUl);
    } while (assumed != old);
    return as_double(old);
}

__kernel void benchmark00(__global double* output, int iterations, int opsPerIter)
{
    // linear thread id (1D expected)
    uint tid = (uint)get_global_id(0);

    // volatile verhindert aggressive Optimierungen
    volatile double acc = 1.23456789 + (double)tid;

    // Workload: opsPerIter FMA-ähnliche Operationen pro Iteration
    for (int i = 0; i < iterations; ++i)
    {
        for (int k = 0; k < opsPerIter; ++k)
        {
            // einfache FMA-ähnliche Kette
            acc = acc * 1.00000011921 + 0.000000000071;
        }
    }

    // Anzahl FLOPs, die dieser Thread ausgeführt hat (2 FLOPs pro "mul+add")
    double threadFlops = (double)iterations * (double)opsPerIter * 2.0;

    // Atomar zum globalen Zähler hinzufügen
    atomicAddDouble(output, threadFlops);

    // kleine Verwendung von acc verhindert komplette Optimierung (volatile hilft bereits)
    if (acc > 1e300)
    {
        // no-op
        output[0] += 0.0;
    }
}