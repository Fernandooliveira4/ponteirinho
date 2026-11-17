using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ponteirinho
{
    // Os nomes ficam salvos no settings.json: não renomeie os que já existem.
    internal enum ClickSoundKind
    {
        Off,
        // Suaves
        Pop,
        Toque,
        Bolha,
        Clique,
        Boop,
        Madeira,
        // Batidas e teclas
        Toc,
        Tecla,
        Teclado,
        MaquinaDeEscrever,
        Estalo,
        // Divertidos
        Videogame,
        Moeda,
        Laser,
        Brinquedo,
        Pato,
        Passarinho,
    }

    /// <summary>
    /// Barulhinho do clique. Os sons são sintetizados na hora (sem arquivos de áudio)
    /// e tocados pelo Windows de forma assíncrona, para não atrasar o mouse.
    /// Várias receitas vêm do script make-sounds.py do TinyHand (licença MIT).
    /// </summary>
    internal sealed class ClickSound : IDisposable
    {
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;
        private const uint SND_MEMORY = 0x0004;
        private const int Rate = 44100;

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

        private byte[] _wav;
        private GCHandle _pin; // o Windows lê o áudio direto da memória, então ela não pode se mover

        public static readonly double[] Volumes = { 0.22, 0.45, 0.8 };

        public void Configure(ClickSoundKind kind, int volumeIndex)
        {
            Release();
            if (kind == ClickSoundKind.Off) return;
            double amp = Volumes[Math.Clamp(volumeIndex, 0, Volumes.Length - 1)];
            _wav = BuildWav(kind, amp);
            _pin = GCHandle.Alloc(_wav, GCHandleType.Pinned);
        }

        public void Play()
        {
            if (_wav == null) return;
            PlaySound(_pin.AddrOfPinnedObject(), IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }

        private void Release()
        {
            // Para qualquer som tocando antes de soltar a memória dele.
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            if (_pin.IsAllocated) _pin.Free();
            _wav = null;
        }

        public void Dispose() => Release();

        // ---------------- Receitas ----------------

        /// <summary>Gera o som já normalizado e o devolve como arquivo WAV (16 bits, mono).</summary>
        internal static byte[] BuildWav(ClickSoundKind kind, double amp)
        {
            double[] s = Recipe(kind);

            // Normaliza o pico. Ondas quadradas (videogame) e tons longos e agudos soam mais altos,
            // então ficam mais baixos para todos parecerem do mesmo volume.
            double gain = kind switch
            {
                ClickSoundKind.Videogame or ClickSoundKind.Moeda or ClickSoundKind.Laser => 0.5,
                ClickSoundKind.Brinquedo or ClickSoundKind.Passarinho => 0.55,
                _ => 0.9,
            };
            double peak = 0;
            foreach (double v in s) peak = Math.Max(peak, Math.Abs(v));
            double scale = peak > 0 ? gain / peak : 0;

            int n = s.Length;
            int fade = (int)(Rate * 0.004);
            var samples = new short[n];
            for (int i = 0; i < n; i++)
            {
                double v = s[i] * scale;
                if (n - i < fade) v *= (double)(n - i) / fade; // termina sem estalo
                samples[i] = (short)Math.Clamp(v * amp * short.MaxValue, short.MinValue, short.MaxValue);
            }
            return ToWav(samples);
        }

        private static double[] Recipe(ClickSoundKind kind)
        {
            switch (kind)
            {
                case ClickSoundKind.Toque:
                {
                    // Batidinha de madeira: dois tons agudos que somem rápido.
                    var x = T(0.07); var nz = Noise(x.Length, 7);
                    return Map(x, (t, i) => (0.6 * Sin(1650 * t) + 0.4 * Sin(2480 * t)) * Math.Exp(-t / 0.012)
                        + nz[i] * 0.45 * Math.Exp(-t / 0.0015));
                }
                case ClickSoundKind.Bolha:
                {
                    // "Bloop": tom que sobe.
                    var x = T(0.12);
                    var ph = Sweep(Map(x, (t, _) => 380 + 900 * (1 - Math.Exp(-t / 0.03))));
                    return Map(x, (t, i) => Math.Sin(ph[i]) * Math.Exp(-t / 0.04) * (1 - Math.Exp(-t / 0.003)));
                }
                case ClickSoundKind.Clique:
                {
                    // Clique de mouse: chiado agudo + um tom bem curto.
                    var x = T(0.05); var nz = Noise(x.Length, 1);
                    return Map(x, (t, i) => ((nz[i] - (i > 0 ? nz[i - 1] : 0)) * 0.6 + Sin(3200 * t) * 0.5) * Env(t, 0.0004, 0.006));
                }
                case ClickSoundKind.Boop:
                {
                    // Tom grave e redondo que desce um pouquinho.
                    var x = T(0.14); double last = x[x.Length - 1];
                    var ph = Sweep(Map(x, (t, _) => 260 - 70 * t / last));
                    return Map(x, (t, i) => (Math.Sin(ph[i]) + 0.2 * Math.Sin(2 * ph[i])) * Env(t, 0.003, 0.045));
                }
                case ClickSoundKind.Madeira:
                {
                    // Bloco de madeira (percussão).
                    var x = T(0.12); var nz = Noise(x.Length, 2);
                    return Map(x, (t, i) => (Sin(820 * t) + 0.45 * Sin(2350 * t) + 0.15 * Sin(4100 * t)) * Env(t, 0.0006, 0.028)
                        + nz[i] * Env(t, 0.0003, 0.002) * 0.4);
                }
                case ClickSoundKind.Toc:
                {
                    var x = T(0.09); var nz = Bandpass(Noise(x.Length, 3), 800, 4000);
                    return Map(x, (t, i) => (Sin(520 * t) + 0.4 * Sin(1460 * t)) * Env(t, 0.0005, 0.018)
                        + 0.3 * nz[i] * Env(t, 0.0002, 0.002));
                }
                case ClickSoundKind.Tecla:
                {
                    // Tecla de teclado mecânico: batida grave + estalo.
                    var x = T(0.08); var nz = Bandpass(Noise(x.Length, 5), 1200, 6000);
                    return Map(x, (t, i) => 0.8 * Sin(150 * t) * Env(t, 0.0005, 0.012) + nz[i] * Env(t, 0.0002, 0.004));
                }
                case ClickSoundKind.Teclado:
                {
                    // "Thock": tecla mais grave e abafada (receita própria).
                    var x = T(0.1); var nz = Bandpass(Noise(x.Length, 11), 300, 2200);
                    var ph = Sweep(Map(x, (t, _) => 210 - 60 * (1 - Math.Exp(-t / 0.02))));
                    return Map(x, (t, i) => Math.Sin(ph[i]) * Env(t, 0.0008, 0.022) + 0.7 * nz[i] * Env(t, 0.0002, 0.005));
                }
                case ClickSoundKind.MaquinaDeEscrever:
                {
                    var x = T(0.1); int n = x.Length;
                    var strike = Bandpass(Noise(n, 6), 2000, 9000);
                    int k = (int)(Rate * 0.018);
                    var late = Bandpass(Noise(n - k, 7), 3000, 9000);
                    return Map(x, (t, i) => strike[i] * Env(t, 0.0001, 0.003)
                        + 0.6 * Sin(380 * t) * Env(t, 0.001, 0.02)
                        + (i >= k ? late[i - k] * Env(x[i - k], 0.0001, 0.002) * 0.5 : 0));
                }
                case ClickSoundKind.Estalo:
                {
                    // Estalo de dedos.
                    var x = T(0.06); var nz = Bandpass(Noise(x.Length, 8), 1500, 7000);
                    return Map(x, (t, i) => nz[i] * Env(t, 0.0002, 0.006) + 0.4 * Sin(2100 * t) * Env(t, 0.0002, 0.004));
                }
                case ClickSoundKind.Videogame:
                {
                    // "Blip" de 8 bits.
                    var x = T(0.07);
                    return Map(x, (t, _) => Math.Sign(Sin(988 * t)) * Env(t, 0.0005, 0.03));
                }
                case ClickSoundKind.Moeda:
                {
                    // Moedinha de videogame: duas notas.
                    var x = T(0.18);
                    var ph = Sweep(Map(x, (t, _) => t < 0.05 ? 988 : 1319));
                    return Map(x, (t, i) => Math.Sign(Math.Sin(ph[i])) * Env(t, 0.0005, 0.07));
                }
                case ClickSoundKind.Laser:
                {
                    // "Piu" de nave espacial.
                    var x = T(0.12);
                    var ph = Sweep(Map(x, (t, _) => 2400 * Math.Exp(-t / 0.03) + 300));
                    return Map(x, (t, i) => Math.Sign(Math.Sin(ph[i])) * Env(t, 0.0005, 0.05));
                }
                case ClickSoundKind.Brinquedo:
                {
                    // Brinquedo de apertar (squeak).
                    var x = T(0.13); double last = x[x.Length - 1];
                    var ph = Sweep(Map(x, (t, _) => 1150 + 650 * Math.Sin(Math.PI * t / last) + 30 * Sin(38 * t)));
                    return Map(x, (t, i) => (Math.Sin(ph[i]) + 0.3 * Math.Sin(2 * ph[i])) * Math.Sin(Math.PI * Math.Min(1, t / last)) * Math.Exp(-t / 0.09));
                }
                case ClickSoundKind.Pato:
                {
                    var x = T(0.14); double last = x[x.Length - 1];
                    var ph = Sweep(Map(x, (t, _) => 520 - 120 * t / last));
                    var s = Map(x, (t, i) =>
                    {
                        double sum = 0;
                        for (int k = 1; k <= 8; k++) sum += Math.Sin(k * ph[i]) / k;
                        return sum;
                    });
                    var b = Bandpass(s, 600, 2600);
                    return Map(x, (t, i) => b[i] * Env(t, 0.004, 0.06));
                }
                case ClickSoundKind.Passarinho:
                {
                    var x = T(0.09); double last = x[x.Length - 1];
                    var ph = Sweep(Map(x, (t, _) => 2600 + 1400 * Math.Sin(Math.PI * t / last)));
                    return Map(x, (t, i) => Math.Sin(ph[i]) * Math.Sin(Math.PI * t / last) * Math.Exp(-t / 0.06));
                }
                default:
                {
                    // Pop suave: tom que desce rápido + um estalinho no começo.
                    var x = T(0.09); var nz = Noise(x.Length, 7);
                    var ph = Sweep(Map(x, (t, _) => 260 + 640 * Math.Exp(-t / 0.018)));
                    return Map(x, (t, i) => Math.Sin(ph[i]) * Math.Exp(-t / 0.028) + nz[i] * 0.3 * Math.Exp(-t / 0.002));
                }
            }
        }

        // ---------------- Ferramentas de síntese ----------------

        /// <summary>Instantes (em segundos) de cada amostra de um som com essa duração.</summary>
        private static double[] T(double seconds)
        {
            var x = new double[(int)(Rate * seconds)];
            for (int i = 0; i < x.Length; i++) x[i] = (double)i / Rate;
            return x;
        }

        private static double[] Map(double[] x, Func<double, int, double> f)
        {
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = f(x[i], i);
            return y;
        }

        private static double Sin(double cyclesPerSecondTimesT) => Math.Sin(2 * Math.PI * cyclesPerSecondTimesT);

        /// <summary>Envelope: sobe em <paramref name="attack"/> segundos e decai exponencialmente.</summary>
        private static double Env(double t, double attack, double decay) =>
            Math.Min(1, t / Math.Max(attack, 1e-6)) * Math.Exp(-t / decay);

        /// <summary>Fase acumulada de uma frequência que muda com o tempo.</summary>
        private static double[] Sweep(double[] freq)
        {
            var ph = new double[freq.Length];
            double acc = 0;
            for (int i = 0; i < freq.Length; i++)
            {
                acc += 2 * Math.PI * freq[i] / Rate;
                ph[i] = acc;
            }
            return ph;
        }

        /// <summary>Ruído branco gaussiano, sempre igual para a mesma semente.</summary>
        private static double[] Noise(int n, int seed)
        {
            var rnd = new Random(seed);
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble();
                y[i] = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }
            return y;
        }

        /// <summary>Deixa passar só as frequências entre lo e hi (filtro pela FFT).</summary>
        private static double[] Bandpass(double[] sig, double lo, double hi)
        {
            int size = 1;
            while (size < sig.Length) size <<= 1;
            var re = new double[size];
            var im = new double[size];
            Array.Copy(sig, re, sig.Length);

            Fft(re, im, false);
            for (int k = 0; k < size; k++)
            {
                double f = (k <= size / 2 ? k : size - k) * (double)Rate / size;
                if (f < lo || f > hi) re[k] = im[k] = 0;
            }
            Fft(re, im, true);

            var y = new double[sig.Length];
            Array.Copy(re, y, sig.Length);
            return y;
        }

        /// <summary>FFT complexa (radix 2, no próprio lugar).</summary>
        private static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci;
                        double ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
            if (inverse)
            {
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
            }
        }

        private static byte[] ToWav(short[] samples)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataBytes = samples.Length * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + dataBytes);
            w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);      // PCM
            w.Write((short)1);      // mono
            w.Write(Rate);
            w.Write(Rate * 2);      // bytes por segundo
            w.Write((short)2);      // bytes por amostra
            w.Write((short)16);     // bits
            w.Write("data"u8.ToArray());
            w.Write(dataBytes);
            foreach (var s in samples) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }
    }
}
