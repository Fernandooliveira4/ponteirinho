using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Ponteirinho
{
    /// <summary>Um risco feito com o mouse. Os pontos ficam em pixels físicos da tela.</summary>
    internal sealed class InkStroke
    {
        private const int MaxPoints = 4000;

        public readonly List<Point> Points = new List<Point>();
        public Color Color;
        public double Width;      // unidades do WPF
        public double EndedAt = -1; // segundos do relógio do app; < 0 = ainda desenhando
        public int Version;       // muda a cada ponto novo, para as janelas saberem que precisam redesenhar
        public double Opacity = 1;

        public void Add(double x, double y)
        {
            if (Points.Count > 0)
            {
                var last = Points[Points.Count - 1];
                if (Math.Abs(x - last.X) + Math.Abs(y - last.Y) < 2) return;
            }
            if (Points.Count >= MaxPoints) return;
            Points.Add(new Point(x, y));
            Version++;
        }

        /// <summary>
        /// Caminho suavizado (curvas entre os pontos médios), já convertido para as coordenadas
        /// de uma janela: origem em (ox, oy) e escala dpi.
        /// </summary>
        public Geometry BuildGeometry(double ox, double oy, double dpi)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                Point P(int i) => new Point((Points[i].X - ox) / dpi, (Points[i].Y - oy) / dpi);

                var first = P(0);
                ctx.BeginFigure(first, false, false);
                if (Points.Count == 1)
                {
                    // Um ponto só ainda vira uma bolinha (as pontas são redondas).
                    ctx.LineTo(new Point(first.X + 0.01, first.Y), true, false);
                }
                else
                {
                    var prev = first;
                    for (int i = 1; i < Points.Count; i++)
                    {
                        var p = P(i);
                        var mid = new Point((prev.X + p.X) / 2, (prev.Y + p.Y) / 2);
                        ctx.QuadraticBezierTo(prev, mid, true, true);
                        prev = p;
                    }
                    ctx.LineTo(prev, true, true);
                }
            }
            g.Freeze();
            return g;
        }
    }
}
