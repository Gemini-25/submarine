namespace DeepLine.Audio.Tests;

/// <summary>시험용 렌더와 간단한 신호 분석(FFT, 대역 에너지, 포락선 변조, 첨도).</summary>
public static class Signal
{
    public static float[] Render(Voice v, float seconds, int rate = 22050, int block = 512, Action<Voice>? poke = null, float pokeEvery = 0.25f)
    {
        var s = new float[(int)(seconds * rate)];
        int pokeSamples = (int)(pokeEvery * rate);
        int nextPoke = 0;
        for (int i = 0; i < s.Length; i += block)
        {
            if (poke != null && i >= nextPoke) { poke(v); nextPoke += pokeSamples; }
            v.Render(s.AsSpan(i, Math.Min(block, s.Length - i)), rate);
        }
        return s;
    }

    public static float MaxStep(ReadOnlySpan<float> s)
    {
        float m = 0;
        for (int i = 1; i < s.Length; i++) m = Math.Max(m, Math.Abs(s[i] - s[i - 1]));
        return m;
    }

    public static double Rms(ReadOnlySpan<float> s)
    {
        double a = 0;
        foreach (var x in s) a += x * x;
        return s.Length == 0 ? 0 : Math.Sqrt(a / s.Length);
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; k++)
                {
                    double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                }
        }
    }

    /// <summary>한 창 크기 n으로 평균한 전력 스펙트럼(Hann 창, 반 겹침). 칸 k의 주파수는 k × rate ÷ n.</summary>
    public static double[] Spectrum(float[] s, int n = 4096)
    {
        var acc = new double[n / 2];
        int frames = 0;
        var re = new double[n];
        var im = new double[n];
        for (int start = 0; start + n <= s.Length; start += n / 2)
        {
            for (int i = 0; i < n; i++)
            {
                re[i] = s[start + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
                im[i] = 0;
            }
            Fft(re, im);
            for (int k = 0; k < n / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
            frames++;
        }
        for (int k = 0; k < acc.Length; k++) acc[k] /= Math.Max(1, frames);
        return acc;
    }

    public static double Band(double[] spec, int rate, double lo, double hi)
    {
        int n = spec.Length * 2;
        double e = 0;
        for (int k = 0; k < spec.Length; k++)
        {
            double f = k * (double)rate / n;
            if (f >= lo && f < hi) e += spec[k];
        }
        return e;
    }

    public static double PeakHz(double[] spec, int rate, double lo, double hi)
    {
        int n = spec.Length * 2;
        int best = 0;
        for (int k = 1; k < spec.Length; k++)
        {
            double f = k * (double)rate / n;
            if (f >= lo && f < hi && spec[k] > spec[best]) best = k;
        }
        return best * (double)rate / n;
    }

    /// <summary>한 극 고역 통과 뒤의 첨도. 딱딱 튀는 소리가 많을수록 크다(가우스 잡음은 3).</summary>
    public static double Kurtosis(float[] s, int rate, double hpHz)
    {
        double a = 1 - Math.Exp(-2 * Math.PI * hpHz / rate), z = 0;
        var h = new double[s.Length];
        for (int i = 0; i < s.Length; i++) { z += a * (s[i] - z); h[i] = s[i] - z; }
        double m2 = h.Average(x => x * x), m4 = h.Average(x => x * x * x * x);
        return m4 / (m2 * m2 + 1e-30);
    }

    /// <summary>
    /// lo~hi 대역의 포락선(20ms 창의 RMS)이 fLo~fHi Hz로 얼마나 출렁이는가. 포락선 평균에 대한 그 대역 변조 성분의 비.
    /// </summary>
    public static double EnvelopeModulation(float[] s, int rate, double lo, double hi, double fLo, double fHi)
    {
        // 대역 통과: 두 한 극 필터의 차
        double aLo = 1 - Math.Exp(-2 * Math.PI * lo / rate), aHi = 1 - Math.Exp(-2 * Math.PI * hi / rate);
        double zl = 0, zh = 0;
        int win = rate / 50;
        var env = new List<double>();
        double acc = 0;
        for (int i = 0; i < s.Length; i++)
        {
            zh += aHi * (s[i] - zh);
            zl += aLo * (s[i] - zl);
            double b = zh - zl;
            acc += b * b;
            if ((i + 1) % win == 0) { env.Add(Math.Sqrt(acc / win)); acc = 0; }
        }
        double mean = env.Average();
        int n = 1;
        while (n < env.Count) n <<= 1;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < env.Count; i++) re[i] = (env[i] - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (env.Count - 1)));
        Fft(re, im);
        double frameRate = 50, e = 0;
        for (int k = 1; k < n / 2; k++)
        {
            double f = k * frameRate / n;
            if (f >= fLo && f <= fHi) e += re[k] * re[k] + im[k] * im[k];
        }
        return Math.Sqrt(e) / (mean * env.Count + 1e-30);
    }
}
