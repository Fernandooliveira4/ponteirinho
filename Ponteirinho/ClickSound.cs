using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ponteirinho
{
    internal enum ClickSoundKind
    {
        Off,
        Pop,
        Toque,
        Bolha,
    }

    /// <summary>
    /// Barulhinho do clique. Os sons são sintetizados na hora (sem arquivos de áudio)
    /// e tocados pelo Windows de forma assíncrona, para não atrasar o mouse.
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
            _wav = Synthesize(kind, amp);
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

        private static byte[] Synthesize(ClickSoundKind kind, double amp)
        {
            double duration = kind switch
            {
                ClickSoundKind.Toque => 0.07,
                ClickSoundKind.Bolha => 0.12,
                _ => 0.09,
            };
            int n = (int)(Rate * duration);
            var samples = new short[n];
            var rnd = new Random(7);
            double phase = 0;
            double fade = Rate * 0.004;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double noise = rnd.NextDouble() * 2 - 1;
                double v;
                switch (kind)
                {
                    case ClickSoundKind.Toque:
                        // Batidinha de madeira: dois tons agudos que somem rápido.
                        v = (0.6 * Math.Sin(2 * Math.PI * 1650 * t) + 0.4 * Math.Sin(2 * Math.PI * 2480 * t)) * Math.Exp(-t / 0.012)
                            + noise * 0.45 * Math.Exp(-t / 0.0015);
                        break;
                    case ClickSoundKind.Bolha:
                        // "Bloop": tom que sobe.
                        phase += 2 * Math.PI * (380 + 900 * (1 - Math.Exp(-t / 0.03))) / Rate;
                        v = Math.Sin(phase) * Math.Exp(-t / 0.04) * (1 - Math.Exp(-t / 0.003));
                        break;
                    default:
                        // Pop suave: tom que desce rápido + um estalinho no começo.
                        phase += 2 * Math.PI * (260 + 640 * Math.Exp(-t / 0.018)) / Rate;
                        v = Math.Sin(phase) * Math.Exp(-t / 0.028) + noise * 0.3 * Math.Exp(-t / 0.002);
                        break;
                }
                if (n - i < fade) v *= (n - i) / fade; // termina sem estalo
                samples[i] = (short)Math.Clamp(v * amp * short.MaxValue, short.MinValue, short.MaxValue);
            }

            // Arquivo WAV (PCM 16 bits, mono).
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataBytes = n * 2;
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
