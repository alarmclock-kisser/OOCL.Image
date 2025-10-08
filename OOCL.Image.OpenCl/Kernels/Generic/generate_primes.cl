// Einfacher OpenCL Kernel: erzeugt die ersten `length` Primzahlen in `output`.
// Aufruf: globalWorkSize = 1 (1D). Nur ein Work-Item füllt das Array sequenziell.
// Argumentreihenfolge wie gewünscht: (int length, __global int* output)

inline int is_prime(int n)
{
	if (n < 2) return 0;
	if (n == 2) return 1;
	if (n % 2 == 0) return 0;
	int i = 3;
	// sqrt(n) prüfen ohne float: i*i <= n
	while ((long) i * (long) i <= n)
	{
		if (n % i == 0) return 0;
		i += 2;
	}
	return 1;
}

__kernel void generate_primes(const int length, __global int* output)
{
	// Nur ein Work-Item nutzen (global_id 0)
	if (get_global_id(0) != 0) return;

	if (output == 0 || length <= 0) return;

	int count = 0;
	int candidate = 2;

	while (count < length)
	{
		if (is_prime(candidate))
		{
			output[count] = candidate;
			count++;
		}

		// Overflow-Schutz (sehr großes length)
		if (candidate == INT_MAX) break;

		// Nächster Kandidat
		if (candidate == 2)
			candidate = 3;
		else
			candidate += 2; // nur ungerade
	}

	// Falls abgebrochen (overflow) und nicht vollständig:
	for (int i = count; i < length; i++)
	{
		output[i] = 0;
	}
}