using System;
using System.Runtime.InteropServices;

namespace Ponteirinho
{
    /// <summary>
    /// Esconde o cursor do Windows trocando todos os cursores do sistema por um transparente.
    /// A troca não é permanente: Restore() (ou sair da conta/reiniciar) traz os cursores de volta.
    /// </summary>
    internal static class CursorHider
    {
        private const uint IMAGE_CURSOR = 2;
        private const uint SPI_SETCURSORS = 0x0057;

        // Seta, texto, espera, cruz, seta pra cima, redimensionar (4), mover, proibido, mão de link,
        // seta com ampulheta, ajuda, alfinete, pessoa.
        private static readonly uint[] SystemCursors =
        {
            32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646,
            32648, 32649, 32650, 32651, 32671, 32672,
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int width, int height, byte[] andPlane, byte[] xorPlane);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyImage(IntPtr handle, uint type, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSystemCursor(IntPtr cursor, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyCursor(IntPtr cursor);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

        public static bool IsHidden { get; private set; }

        /// <summary>Esconde. Devolve false se o Windows recusou algum cursor (nesse caso restaura tudo).</summary>
        public static bool Hide()
        {
            if (IsHidden) return true;

            // Cursor 32x32 monocromático: máscara AND toda 1 e XOR toda 0 = nada é desenhado.
            var and = new byte[32 * 32 / 8];
            var xor = new byte[32 * 32 / 8];
            for (int i = 0; i < and.Length; i++) and[i] = 0xFF;
            IntPtr blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor);
            if (blank == IntPtr.Zero) return false;

            IsHidden = true;
            bool ok = true;
            foreach (uint id in SystemCursors)
            {
                // SetSystemCursor "consome" o cursor recebido, então cada um ganha uma cópia.
                IntPtr copy = CopyImage(blank, IMAGE_CURSOR, 0, 0, 0);
                if (copy == IntPtr.Zero || !SetSystemCursor(copy, id))
                {
                    if (copy != IntPtr.Zero) DestroyCursor(copy);
                    // Alguns ids (alfinete, pessoa) não existem em versões antigas do Windows; ignora.
                    if (id < 32671) ok = false;
                }
            }
            DestroyCursor(blank);

            if (!ok) Restore();
            return ok;
        }

        /// <summary>Recarrega os cursores do usuário (o esquema configurado no Windows).</summary>
        public static void Restore()
        {
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            IsHidden = false;
        }
    }
}
