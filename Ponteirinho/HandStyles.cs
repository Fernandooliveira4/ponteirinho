using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ponteirinho
{
    internal enum HandKind
    {
        /// <summary>Mão 3D pronta do TinyHand (cor fixa, vareta própria).</summary>
        Image,
        /// <summary>Arte em tons de cinza que o app pinta com a cor escolhida.</summary>
        Tinted,
        /// <summary>Só um ponto de laser.</summary>
        Laser,
    }

    internal sealed class HandStyle
    {
        public string Id { get; }
        public string Label { get; }
        public HandKind Kind { get; }
        public string Resource { get; }
        public string Ink { get; }      // cor da onda do clique (mãos do TinyHand)
        public double TipX { get; }     // mãos pintáveis: pontos na arte de 200 x 260
        public double TipY { get; }
        public double WristX { get; }
        public double WristY { get; }

        private HandStyle(string id, string label, HandKind kind, string resource, string ink,
            double tipX = 0, double tipY = 0, double wristX = 0, double wristY = 0)
        {
            Id = id;
            Label = label;
            Kind = kind;
            Resource = resource;
            Ink = ink;
            TipX = tipX;
            TipY = tipY;
            WristX = wristX;
            WristY = wristY;
        }

        private static HandStyle Th(string name, string label, string ink) =>
            new HandStyle("th-" + name, label, HandKind.Image, $"tinyhand/hand_{name}.png", ink);

        private static HandStyle Tinted(string id, string label, string res, double tx, double ty, double wx, double wy) =>
            new HandStyle(id, label, HandKind.Tinted, res, null, tx, ty, wx, wy);

        public static readonly HandStyle Laser = new HandStyle("laser", "Laser (só um ponto)", HandKind.Laser, null, null);

        /// <summary>As 36 mãos do TinyHand (github.com/nilbuild/tinyhand, licença MIT), na ordem do app original.</summary>
        public static readonly HandStyle[] TinyHand =
        {
            Th("lavender", "Lavanda", "#7C5CE0"),
            Th("glove", "Luva de cursor", "#7C5CE0"),
            Th("coral", "Coral", "#E5484D"),
            Th("plush", "Pelúcia", "#2F86F0"),
            Th("robot", "Robô", "#16A87A"),
            Th("pink", "Chiclete", "#EE4F98"),
            Th("blue", "Céu", "#2F86F0"),
            Th("mint", "Menta", "#16A87A"),
            Th("lemon", "Limão", "#F2B705"),
            Th("black", "Meia-noite", "#1D1D21"),
            Th("chrome", "Cromada", "#6B7380"),
            Th("holo", "Holográfica", "#7C5CE0"),
            Th("fuzzy", "Peludinha", "#7C5CE0"),
            Th("wood", "Madeira", "#9A5E2C"),
            Th("marble", "Mármore", "#6B7380"),
            Th("terrazzo", "Granilite", "#F0561D"),
            Th("glass", "Vidro", "#1597E5"),
            Th("jelly", "Gelatina", "#A33FE6"),
            Th("pastel", "Pastel", "#EE4F98"),
            Th("pixel", "Pixel", "#1D1D21"),
            Th("lowpoly", "Low poly", "#7C5CE0"),
            Th("knit", "Tricô", "#7C5CE0"),
            Th("skeleton", "Esqueleto", "#9A5E2C"),
            Th("astro", "Astronauta", "#2F86F0"),
            Th("space", "Espacial", "#E5484D"),
            Th("monster", "Monstro", "#3E9E2E"),
            Th("water", "Água", "#1597E5"),
            Th("lava", "Lava", "#F0561D"),
            Th("grass", "Grama", "#3E9E2E"),
            Th("cactus", "Cacto", "#3E9E2E"),
            Th("choco", "Chocolate", "#9A5E2C"),
            Th("cloud", "Nuvem", "#2F86F0"),
            Th("mech", "Mecha", "#F0561D"),
            Th("neon", "Neon", "#E0309A"),
            Th("leaf", "Folha", "#3E9E2E"),
            Th("cookie", "Biscoito", "#9A5E2C"),
        };

        /// <summary>Mãos desenhadas aqui (VectorCraft/FigCraft), que aceitam qualquer cor.</summary>
        public static readonly HandStyle[] Paintable =
        {
            Tinted("massinha", "Massinha 3D", "hand-massinha.png", 93, 21, 106, 226),
            Tinted("fofinha", "Fofinha", "hand-fofinha.png", 94, 7, 105, 228),
            Tinted("luva", "Luva de desenho", "hand-luva.png", 94, 8, 104, 236),
            Tinted("classica", "Clássica", "hand-classica.png", 94, 10, 106, 226),
        };

        public static IEnumerable<HandStyle> All
        {
            get
            {
                foreach (var s in TinyHand) yield return s;
                foreach (var s in Paintable) yield return s;
                yield return Laser;
            }
        }

        public static HandStyle Find(string id)
        {
            foreach (var s in All)
                if (s.Id == id) return s;
            return TinyHand[0];
        }
    }

    /// <summary>Tudo que a janela precisa para desenhar uma mão (em pixels da imagem).</summary>
    internal sealed class HandVisual
    {
        public BitmapSource Image;
        public double TipX, TipY;        // ponta do dedo
        public double AttachX, AttachY;  // onde a vareta continua
        public double BodyWidth;         // largura de referência para o tamanho na tela
        public double BodyFactor;        // fração do "Tamanho" ocupada por BodyWidth
        public BitmapSource RodSlice;    // fatia da vareta original (null = vareta desenhada)
        public double RodWidth;

        // Região da mão (para miniaturas).
        public Int32Rect BodyRect;
    }

    internal static class HandLibrary
    {
        private static readonly Dictionary<string, HandVisual> Cache = new Dictionary<string, HandVisual>();

        public static HandVisual Get(HandStyle style, Color color)
        {
            if (style.Kind == HandKind.Laser) return null;
            string key = style.Kind == HandKind.Tinted ? style.Id + color : style.Id;
            if (Cache.TryGetValue(key, out var hit)) return hit;

            var v = style.Kind == HandKind.Tinted ? BuildTinted(style, color) : BuildImage(style);
            Cache[key] = v;
            return v;
        }

        private static HandVisual BuildTinted(HandStyle st, Color color)
        {
            var img = HandTinter.Get(st, color); // 400 x 520 (arte de 200 x 260 com escala 2)
            return new HandVisual
            {
                Image = img,
                TipX = st.TipX * 2,
                TipY = st.TipY * 2,
                AttachX = st.WristX * 2,
                AttachY = st.WristY * 2,
                BodyWidth = 400,
                BodyFactor = 1.0,
                BodyRect = new Int32Rect(0, 0, img.PixelWidth, img.PixelHeight),
            };
        }

        /// <summary>
        /// Mede a imagem como o TinyHand faz: linha a linha, conta os pixels opacos para achar
        /// a ponta do dedo, o punho e a vareta.
        /// </summary>
        private static HandVisual BuildImage(HandStyle st)
        {
            var bmp = Load(st.Resource);
            int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bmp.CopyPixels(px, stride, 0);

            var counts = new int[h];
            for (int y = 0; y < h; y++)
            {
                int n = 0;
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 128) n++;
                counts[y] = n;
            }

            double Center(int y)
            {
                int lo = -1, hi = -1;
                for (int x = 0; x < w; x++)
                {
                    if (px[y * stride + x * 4 + 3] <= 128) continue;
                    if (lo < 0) lo = x;
                    hi = x;
                }
                return lo < 0 ? w / 2.0 : (lo + hi) / 2.0;
            }

            int top = Array.FindIndex(counts, c => c > 0);
            if (top < 0) top = 0;
            int searchEnd = Math.Min(h, top + (int)(0.45 * h));
            int maxW = 1;
            for (int y = top; y < searchEnd; y++) maxW = Math.Max(maxW, counts[y]);

            int fist = top;
            while (fist < h - 1 && counts[fist] <= 0.6 * maxW) fist++;
            int wrist = fist;
            while (wrist < h - 1 && counts[wrist] >= 0.35 * maxW) wrist++;

            int rodW = Math.Max(2, counts[Math.Min(h - 1, wrist + h / 50)]);
            int lastRow = h - 1;
            while (lastRow > 0 && counts[lastRow] == 0) lastRow--;
            int from = Math.Min(h - 1, wrist + h / 33);
            int rodEnd = lastRow;
            for (int y = from; y < h; y++)
            {
                if (counts[y] > 1.8 * rodW)
                {
                    rodEnd = y;
                    break;
                }
            }

            // A imagem da mão vai até um pouco abaixo do punho; dali em diante a vareta é esticada.
            int rodCut = Math.Min(h - 1, wrist + Math.Max(4, (rodEnd - wrist) / 4));
            int sliceY = Math.Min(h - 1, wrist + (rodEnd - wrist) / 2);
            double rodX = Center(sliceY);

            int minX = w, maxX = 0;
            for (int y = top; y < wrist; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                    }
            if (maxX <= minX)
            {
                minX = 0;
                maxX = w - 1;
            }

            var hand = new CroppedBitmap(bmp, new Int32Rect(0, 0, w, rodCut + 1));
            hand.Freeze();

            int sliceX = Math.Max(0, (int)Math.Round(rodX - rodW / 2.0) - 1);
            int sliceW = Math.Min(w - sliceX, rodW + 2);
            var slice = new CroppedBitmap(bmp, new Int32Rect(sliceX, sliceY, sliceW, 1));
            slice.Freeze();

            return new HandVisual
            {
                Image = hand,
                TipX = Center(Math.Min(h - 1, top + h / 200)),
                TipY = top,
                AttachX = rodX,
                AttachY = rodCut - 1,
                BodyWidth = maxX - minX,
                BodyFactor = 0.85,
                RodSlice = slice,
                RodWidth = sliceW,
                BodyRect = new Int32Rect(minX, top, maxX - minX + 1, Math.Max(1, wrist - top)),
            };
        }

        private static BitmapSource Load(string resource)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Assets/" + resource);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            var bgra = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
            return bgra;
        }

        /// <summary>Miniatura quadrada (menu e ícone da bandeja).</summary>
        public static System.Drawing.Bitmap Thumbnail(HandStyle style, Color color, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(System.Drawing.Color.Transparent);

            if (style.Kind == HandKind.Laser)
            {
                var dc = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                float r = size * 0.32f, cx = size / 2f;
                using var glow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(90, dc));
                g.FillEllipse(glow, cx - r * 1.4f, cx - r * 1.4f, r * 2.8f, r * 2.8f);
                using var dot = new System.Drawing.SolidBrush(dc);
                g.FillEllipse(dot, cx - r, cx - r, r * 2, r * 2);
                using var core = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(230, 255, 255, 255));
                g.FillEllipse(core, cx - r * 0.4f, cx - r * 0.4f, r * 0.8f, r * 0.8f);
                return bmp;
            }

            var v = Get(style, color);
            var body = new CroppedBitmap(v.Image, v.BodyRect);
            using var src = ToDrawing(body);
            double scale = Math.Min((double)size / src.Width, (double)size / src.Height);
            int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
            g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
            return bmp;
        }

        private static System.Drawing.Bitmap ToDrawing(BitmapSource source)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            var ms = new System.IO.MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms);
        }
    }

    internal static class HandColors
    {
        public static readonly (string Name, string Hex)[] All =
        {
            ("Lavanda", "#B7A4F2"),
            ("Rosa", "#F6A5C6"),
            ("Vermelho", "#FF6B6B"),
            ("Laranja", "#FFAE5E"),
            ("Amarelo", "#FFD45C"),
            ("Verde", "#86DB9C"),
            ("Azul", "#7DB8FF"),
            ("Branco", "#F2F2F2"),
            ("Preto", "#3A3A3A"),
            ("Pele clara", "#F5C9A3"),
            ("Pele média", "#D49A6A"),
            ("Pele escura", "#8D5A3B"),
        };

        public static Color Parse(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(All[0].Hex);
            }
        }
    }

    /// <summary>
    /// "Pinta" a arte cinza com a cor escolhida (mapa de gradiente):
    /// cinza médio vira a cor exata, mais escuro vira sombra, mais claro vira brilho.
    /// Pixels que já são coloridos (ex.: o punho azul da Clássica) ficam como estão.
    /// </summary>
    internal static class HandTinter
    {
        private static readonly Dictionary<string, BitmapSource> Gray = new Dictionary<string, BitmapSource>();

        public static BitmapSource Get(HandStyle style, Color color) => Tint(LoadGray(style), color);

        private static BitmapSource LoadGray(HandStyle style)
        {
            if (Gray.TryGetValue(style.Id, out var g)) return g;
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Assets/" + style.Resource);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            Gray[style.Id] = img;
            return img;
        }

        private static BitmapSource Tint(BitmapSource source, Color c)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);

            // Tom de sombra: a própria cor bem escura.
            double dr = c.R * 0.12, dg = c.G * 0.12, db = c.B * 0.12;

            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a == 0) continue;
                byte b = px[i], gg = px[i + 1], r = px[i + 2];

                int max = Math.Max(r, Math.Max(gg, b)), min = Math.Min(r, Math.Min(gg, b));
                if (max - min > 14) continue; // pixel colorido: mantém

                double l = (r + gg + b) / (3.0 * 255.0);
                double nr, ng, nb;
                if (l <= 0.5)
                {
                    double t = l / 0.5;
                    nr = dr + (c.R - dr) * t;
                    ng = dg + (c.G - dg) * t;
                    nb = db + (c.B - db) * t;
                }
                else
                {
                    double t = (l - 0.5) / 0.5;
                    nr = c.R + (255 - c.R) * t;
                    ng = c.G + (255 - c.G) * t;
                    nb = c.B + (255 - c.B) * t;
                }
                px[i] = (byte)Math.Round(nb);
                px[i + 1] = (byte)Math.Round(ng);
                px[i + 2] = (byte)Math.Round(nr);
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
            bmp.Freeze();
            return bmp;
        }

        public static System.Drawing.Bitmap Swatch(Color color, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(color.R, color.G, color.B));
            using var edge = new System.Drawing.Pen(System.Drawing.Color.FromArgb(120, 0, 0, 0), 1);
            g.FillEllipse(fill, 1, 1, size - 3, size - 3);
            g.DrawEllipse(edge, 1, 1, size - 3, size - 3);
            return bmp;
        }
    }
}
