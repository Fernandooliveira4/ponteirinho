using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ponteirinho
{
    /// <summary>O que desenhar num quadro: posição, tamanho, lado da vareta e animação de clique.</summary>
    internal struct PointerFrame
    {
        public double CursorX;   // pixels físicos da tela
        public double CursorY;
        public double HandWidth; // pixels lógicos
        public StickSide Side;
        public double Press;     // 1 = normal; menor = mão "cutucando"
        public double Ripple;    // 0..1 = progresso da onda do clique; < 0 = sem onda
    }

    /// <summary>
    /// Janela transparente do tamanho de um monitor: fica sempre por cima,
    /// deixa os cliques passarem e desenha a vareta e a mão (ou o laser).
    /// </summary>
    internal sealed class OverlayWindow : Window
    {
        // Vareta de madeira: contorno, miolo e brilho.
        private static readonly Brush WoodEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x1D, 0x14)));
        private static readonly Brush WoodCore = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0x8B, 0x4F)));
        private static readonly Brush WoodShine = Frozen(new SolidColorBrush(Color.FromArgb(150, 0xF6, 0xD2, 0xA4)));
        // Vareta preta e brilhante.
        private static readonly Brush BlackEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E)));
        private static readonly Brush BlackCore = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)));
        private static readonly Brush BlackShine = Frozen(new SolidColorBrush(Color.FromArgb(110, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)));

        private readonly Canvas _rig = new Canvas();
        private readonly Polygon _stickShadow = new Polygon { Fill = ShadowBrush };
        private readonly Polygon _stickOuter = new Polygon { Fill = BlackEdge };
        private readonly Polygon _stickInner = new Polygon { Fill = BlackCore };
        private readonly Polygon _stickShine = new Polygon { Fill = BlackShine };
        private readonly Rectangle _rod = new Rectangle(); // vareta original da imagem, esticada
        private readonly TranslateTransform _rodCenter = new TranslateTransform();
        private readonly RotateTransform _rodRotate = new RotateTransform();
        private readonly TranslateTransform _rodPos = new TranslateTransform();
        private readonly Image _hand = new Image { Stretch = Stretch.Fill };
        private readonly Rectangle _handShadow = new Rectangle { Fill = ShadowBrush };
        private readonly Ellipse _laser = new Ellipse();
        private readonly Ellipse _ripple = new Ellipse { StrokeThickness = 3, Visibility = Visibility.Collapsed };

        private readonly TranslateTransform _toTip = new TranslateTransform();
        private readonly ScaleTransform _press = new ScaleTransform(1, 1);
        private readonly RotateTransform _rotate = new RotateTransform();
        private readonly TranslateTransform _toCursor = new TranslateTransform();

        private static readonly Brush InkShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)));

        private readonly Canvas _inkLayer = new Canvas();
        private readonly Dictionary<InkStroke, (Path Line, Path Shadow, int Version)> _inkPaths =
            new Dictionary<InkStroke, (Path, Path, int)>();

        private IntPtr _hwnd;
        private bool _rigVisible = true;
        private HandStyle _style = HandStyle.TinyHand[0];
        private HandVisual _visual;

        public System.Drawing.Rectangle PixelBounds { get; }

        public OverlayWindow(System.Drawing.Rectangle pixelBounds)
        {
            PixelBounds = pixelBounds;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Title = "Ponteirinho";

            // Posição provisória; a real (em pixels físicos) é aplicada em ApplyBounds.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = pixelBounds.X;
            Top = pixelBounds.Y;
            Width = pixelBounds.Width;
            Height = pixelBounds.Height;

            var handTransform = new TransformGroup();
            handTransform.Children.Add(_toTip);
            handTransform.Children.Add(_press);
            handTransform.Children.Add(_rotate);
            handTransform.Children.Add(_toCursor);
            _hand.RenderTransform = handTransform;

            // Sombra barata: a silhueta da mão em preto translúcido, deslocada para baixo e à direita.
            var shadowTransform = new TransformGroup();
            shadowTransform.Children.Add(handTransform);
            shadowTransform.Children.Add(new TranslateTransform(5, 7));
            _handShadow.RenderTransform = shadowTransform;

            var rodTransform = new TransformGroup();
            rodTransform.Children.Add(_rodCenter);
            rodTransform.Children.Add(_rodRotate);
            rodTransform.Children.Add(_rodPos);
            _rod.RenderTransform = rodTransform;

            _rig.Children.Add(_stickShadow);
            _rig.Children.Add(_handShadow);
            _rig.Children.Add(_stickOuter);
            _rig.Children.Add(_stickInner);
            _rig.Children.Add(_stickShine);
            _rig.Children.Add(_rod);
            _rig.Children.Add(_hand);
            _rig.Children.Add(_laser);
            _rig.Children.Add(_ripple);

            var root = new Canvas { IsHitTestVisible = false };
            root.Children.Add(_inkLayer); // riscos ficam embaixo da mão
            root.Children.Add(_rig);
            Content = root;

            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(ApplyBounds));
        }

        private static Brush Frozen(Brush b)
        {
            b.Freeze();
            return b;
        }

        /// <summary>Troca o tipo/cor da mão e a cor da vareta.</summary>
        public void ApplyStyle(HandStyle style, HandVisual visual, Color color, bool woodStick, Color rippleColor)
        {
            _stickOuter.Fill = woodStick ? WoodEdge : BlackEdge;
            _stickInner.Fill = woodStick ? WoodCore : BlackCore;
            _stickShine.Fill = woodStick ? WoodShine : BlackShine;

            _style = style;
            _visual = visual;
            bool laser = style.Kind == HandKind.Laser;
            bool ownRod = !laser && visual.RodSlice != null;

            Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
            _stickShadow.Visibility = Show(!laser);
            _hand.Visibility = _handShadow.Visibility = Show(!laser);
            _stickOuter.Visibility = _stickInner.Visibility = _stickShine.Visibility = Show(!laser && !ownRod);
            _rod.Visibility = Show(ownRod);
            _laser.Visibility = Show(laser);

            if (!laser)
            {
                _hand.Source = visual.Image;
                _handShadow.OpacityMask = new ImageBrush(visual.Image);
                if (ownRod)
                {
                    var rodBrush = new ImageBrush(visual.RodSlice) { Stretch = Stretch.Fill };
                    rodBrush.Freeze();
                    _rod.Fill = rodBrush;
                }
            }

            // Laser: miolo branco, cor no meio e um brilho que se apaga nas bordas.
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 0.28));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(150, color.R, color.G, color.B), 0.45));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
            glow.Freeze();
            _laser.Fill = glow;

            var ring = new SolidColorBrush(rippleColor);
            ring.Freeze();
            _ripple.Stroke = ring;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetExStyle(_hwnd);
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
            Native.SetExStyle(_hwnd, ex);
            ApplyBounds();
        }

        /// <summary>Encaixa a janela exatamente no monitor, em pixels físicos.</summary>
        private void ApplyBounds()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST,
                PixelBounds.X, PixelBounds.Y, PixelBounds.Width, PixelBounds.Height, Native.SWP_NOACTIVATE);
        }

        /// <summary>Garante que continuamos por cima de outras janelas "sempre visíveis".</summary>
        public void ReassertTopmost()
        {
            if (_hwnd == IntPtr.Zero || !IsVisible) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public bool Contains(int x, int y) => PixelBounds.Contains(x, y);

        public void HidePointer()
        {
            if (!_rigVisible) return;
            _rig.Visibility = Visibility.Collapsed;
            _rigVisible = false;
        }

        public void ShowPointer(in PointerFrame f)
        {
            if (!_rigVisible)
            {
                _rig.Visibility = Visibility.Visible;
                _rigVisible = true;
            }

            // Pixels físicos -> unidades do WPF (que acompanham a escala do monitor).
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double px = (f.CursorX - PixelBounds.X) / dpi;
            double py = (f.CursorY - PixelBounds.Y) / dpi;
            double k = f.HandWidth / 72.0;

            if (_style.Kind == HandKind.Laser || _visual == null)
                DrawLaser(px, py, k, f.Press);
            else
                DrawHand(px, py, f, PixelBounds.Width / dpi, PixelBounds.Height / dpi);

            // Onda na ponta do dedo quando clica.
            if (f.Ripple >= 0)
            {
                double r = (6 + 26 * f.Ripple) * k;
                _ripple.Width = _ripple.Height = r * 2;
                Canvas.SetLeft(_ripple, px - r);
                Canvas.SetTop(_ripple, py - r);
                _ripple.Opacity = 0.8 * (1 - f.Ripple);
                _ripple.Visibility = Visibility.Visible;
            }
            else if (_ripple.Visibility == Visibility.Visible)
            {
                _ripple.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Mostra os riscos atuais: cria, redesenha ou apaga os que mudaram.</summary>
        public void UpdateInk(List<InkStroke> strokes)
        {
            if (_inkPaths.Count == 0 && strokes.Count == 0) return;

            // Riscos que já sumiram.
            if (_inkPaths.Count > 0)
            {
                List<InkStroke> gone = null;
                foreach (var s in _inkPaths.Keys)
                    if (!strokes.Contains(s)) (gone ??= new List<InkStroke>()).Add(s);
                if (gone != null)
                {
                    foreach (var s in gone)
                    {
                        var (line, shadow, _) = _inkPaths[s];
                        _inkLayer.Children.Remove(line);
                        _inkLayer.Children.Remove(shadow);
                        _inkPaths.Remove(s);
                    }
                }
            }

            double dpi = 0;
            foreach (var s in strokes)
            {
                if (!_inkPaths.TryGetValue(s, out var entry))
                {
                    var brush = new SolidColorBrush(s.Color);
                    brush.Freeze();
                    Path Make(Brush b) => new Path
                    {
                        Stroke = b,
                        StrokeThickness = s.Width,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                    };
                    var shadow = Make(InkShadowBrush);
                    shadow.RenderTransform = new TranslateTransform(1.5, 2);
                    var line = Make(brush);
                    _inkLayer.Children.Add(shadow);
                    _inkLayer.Children.Add(line);
                    entry = (line, shadow, -1);
                }

                if (entry.Version != s.Version)
                {
                    if (dpi == 0) dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    var geometry = s.BuildGeometry(PixelBounds.X, PixelBounds.Y, dpi);
                    entry.Line.Data = geometry;
                    entry.Shadow.Data = geometry;
                    entry.Version = s.Version;
                }
                entry.Line.Opacity = entry.Shadow.Opacity = s.Opacity;
                _inkPaths[s] = entry;
            }
        }

        private void DrawLaser(double px, double py, double k, double press)
        {
            // No clique o ponto "pulsa" (cresce um pouco).
            double r = 24 * k * (2 - press);
            _laser.Width = _laser.Height = r * 2;
            Canvas.SetLeft(_laser, px - r);
            Canvas.SetTop(_laser, py - r);
        }

        private void DrawHand(double px, double py, in PointerFrame f, double w, double h)
        {
            var v = _visual;

            // A vareta nasce um pouco fora da tela.
            Point anchor = f.Side switch
            {
                StickSide.Left => new Point(-90, h + 90),
                StickSide.Center => new Point(w / 2, h + 180),
                _ => new Point(w + 90, h + 90),
            };

            // Escala: pixels da imagem -> unidades da tela.
            double s = f.HandWidth * v.BodyFactor / v.BodyWidth;
            double artAngle = Math.Atan2(v.TipY - v.AttachY, v.TipX - v.AttachX);
            double rot = Math.Atan2(py - anchor.Y, px - anchor.X) - artAngle;

            // Mão: ponta do dedo vai para a origem, encolhe (clique), gira e vai até o cursor.
            _hand.Width = _handShadow.Width = v.Image.PixelWidth * s;
            _hand.Height = _handShadow.Height = v.Image.PixelHeight * s;
            _toTip.X = -v.TipX * s;
            _toTip.Y = -v.TipY * s;
            _press.ScaleX = _press.ScaleY = f.Press;
            _rotate.Angle = rot * 180.0 / Math.PI;
            _toCursor.X = px;
            _toCursor.Y = py;

            // Onde a vareta encaixa depois dessas transformações.
            double lx = (v.AttachX - v.TipX) * s * f.Press;
            double ly = (v.AttachY - v.TipY) * s * f.Press;
            double c = Math.Cos(rot), sn = Math.Sin(rot);
            var wrist = new Point(px + lx * c - ly * sn, py + lx * sn + ly * c);

            if (v.RodSlice != null)
            {
                // Vareta da própria imagem: a fatia é esticada do punho até fora da tela.
                double rodW = v.RodWidth * s * f.Press;
                Vector d = anchor - wrist;
                _rod.Width = rodW;
                _rod.Height = d.Length;
                _rodCenter.X = -rodW / 2;
                _rodRotate.Angle = Math.Atan2(d.Y, d.X) * 180.0 / Math.PI - 90;
                _rodPos.X = wrist.X;
                _rodPos.Y = wrist.Y;
                _stickShadow.Points = Taper(wrist, anchor, rodW * 0.9, rodW * 0.9, new Vector(5, 7));
                return;
            }

            // Vareta desenhada, afinando em direção à mão.
            double k = f.HandWidth / 72.0;
            _stickShadow.Points = Taper(wrist, anchor, 9 * k, 17 * k, new Vector(5, 7));
            _stickOuter.Points = Taper(wrist, anchor, 9 * k, 17 * k, default);
            _stickInner.Points = Taper(wrist, anchor, 4 * k, 10 * k, default);
            _stickShine.Points = Taper(wrist, anchor, 1.2 * k, 2.5 * k, Perp(wrist, anchor) * (-1.6 * k));
        }

        private static Vector Perp(Point a, Point b)
        {
            Vector d = b - a;
            if (d.Length < 0.001) return new Vector(0, 0);
            d.Normalize();
            return new Vector(-d.Y, d.X);
        }

        private static PointCollection Taper(Point a, Point b, double widthA, double widthB, Vector offset)
        {
            Vector n = Perp(a, b);
            var pts = new PointCollection(4)
            {
                a + n * (widthA / 2) + offset,
                b + n * (widthB / 2) + offset,
                b - n * (widthB / 2) + offset,
                a - n * (widthA / 2) + offset,
            };
            pts.Freeze();
            return pts;
        }
    }
}
