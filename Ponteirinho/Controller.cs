using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Ponteirinho
{
    /// <summary>
    /// Cérebro do app: lê o mouse a cada quadro, cuida do atalho, do som, do ícone na bandeja
    /// e das janelas transparentes (uma por monitor).
    /// </summary>
    internal sealed class Controller : IDisposable
    {
        private const int HotkeyId = 1;
        private const string HotkeyLabel = "Ctrl+Alt+H";
        private const int DrawHotkeyId = 2;
        private const string DrawHotkeyLabel = "Ctrl+Alt+D";
        private const int DragThreshold = 6;         // pixels que o mouse anda até virar risco
        private const double InkFadeDuration = 0.6;  // segundos
        // Marca os cliques que o próprio Ponteirinho reenvia, para o hook deixá-los passar.
        private static readonly IntPtr ReplayTag = new IntPtr(0x50544E48);
        private const double PressDuration = 0.16;  // segundos
        private const double RippleDuration = 0.38; // segundos
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "Ponteirinho";

        private static readonly (string Label, double Width)[] Sizes =
        {
            ("Pequena", 56),
            ("Média", 72),
            ("Grande", 96),
            ("Enorme", 128),
        };

        private static readonly (string Label, StickSide Side)[] Sides =
        {
            ("Vem do canto inferior direito", StickSide.Right),
            ("Vem do centro, embaixo", StickSide.Center),
            ("Vem do canto inferior esquerdo", StickSide.Left),
        };

        // Header = null: item normal; texto: título cinza de um grupo.
        private static readonly (string Header, string Label, ClickSoundKind Kind)[] SoundKinds =
        {
            (null, "Desligado", ClickSoundKind.Off),
            ("Suaves", "Pop suave", ClickSoundKind.Pop),
            (null, "Toque de madeira", ClickSoundKind.Toque),
            (null, "Bolha", ClickSoundKind.Bolha),
            (null, "Clique de mouse", ClickSoundKind.Clique),
            (null, "Boop grave", ClickSoundKind.Boop),
            (null, "Bloco de madeira", ClickSoundKind.Madeira),
            ("Batidas e teclas", "Toc", ClickSoundKind.Toc),
            (null, "Tecla mecânica", ClickSoundKind.Tecla),
            (null, "Tecla grave (thock)", ClickSoundKind.Teclado),
            (null, "Máquina de escrever", ClickSoundKind.MaquinaDeEscrever),
            (null, "Estalo de dedos", ClickSoundKind.Estalo),
            ("Divertidos", "Videogame (blip)", ClickSoundKind.Videogame),
            (null, "Moedinha", ClickSoundKind.Moeda),
            (null, "Laser (piu!)", ClickSoundKind.Laser),
            (null, "Brinquedo de apertar", ClickSoundKind.Brinquedo),
            (null, "Pato", ClickSoundKind.Pato),
            (null, "Passarinho", ClickSoundKind.Passarinho),
        };

        private static readonly string[] VolumeLabels = { "Volume baixo", "Volume médio", "Volume alto" };

        private static readonly (string Name, string Hex)[] InkColors =
        {
            ("Roxo", "#8754F2"),
            ("Vermelho", "#FF2A1F"),
            ("Laranja", "#FF8A00"),
            ("Amarelo", "#FFD60A"),
            ("Verde", "#1DB954"),
            ("Azul", "#2F7BFF"),
            ("Rosa", "#FF4FA3"),
            ("Branco", "#FFFFFF"),
            ("Preto", "#1A1A1A"),
        };

        private static readonly (string Label, double Seconds)[] InkDurations =
        {
            ("Some depois de 1 segundo", 1),
            ("Some depois de 1,5 segundo", 1.5),
            ("Some depois de 3 segundos", 3),
            ("Some depois de 6 segundos", 6),
        };

        private readonly Settings _settings = Settings.Load();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<OverlayWindow> _overlays = new List<OverlayWindow>();
        private readonly Native.LowLevelMouseProc _mouseProc; // guardado para o GC não coletar
        private readonly DispatcherTimer _topmostTimer;
        private readonly DispatcherTimer _wakeTimer;
        private readonly ClickSound _sound = new ClickSound();

        private bool _enabled;
        private bool _dirty = true;
        private bool _renderLoopOn;
        private double _lastActivity;
        private Native.POINT _lastCursor;
        private double _lastClick = -10;
        private IntPtr _mouseHook = IntPtr.Zero;
        private HwndSource _messageWindow;

        // Desenho
        private readonly List<InkStroke> _strokes = new List<InkStroke>();
        private InkStroke _currentStroke;
        private bool _pressPending; // botão apertado, ainda sem saber se é clique ou risco
        private Native.POINT _pressPoint;
        private WinForms.ToolStripMenuItem _drawItem;
        private readonly List<WinForms.ToolStripMenuItem> _inkColorItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _inkTimeItems = new List<WinForms.ToolStripMenuItem>();

        private WinForms.NotifyIcon _tray;
        private IntPtr _trayIconHandle = IntPtr.Zero;
        private WinForms.ToolStripMenuItem _toggleItem;
        private WinForms.ToolStripMenuItem _colorMenu;
        private WinForms.ToolStripMenuItem _startupItem;
        private readonly List<WinForms.ToolStripMenuItem> _styleItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _colorItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _sizeItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _sideItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _soundItems = new List<WinForms.ToolStripMenuItem>();
        private readonly List<WinForms.ToolStripMenuItem> _volumeItems = new List<WinForms.ToolStripMenuItem>();

        public Controller()
        {
            _mouseProc = MouseHook;
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _topmostTimer.Tick += (s, e) => _overlays.ForEach(o => o.ReassertTopmost());

            // Rede de segurança: se o cursor mudar sem passar pelo hook (ex.: movido por outro programa),
            // acorda o desenho mesmo assim.
            _wakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _wakeTimer.Tick += (s, e) =>
            {
                if (_enabled && Native.GetCursorPos(out var c) && (c.X != _lastCursor.X || c.Y != _lastCursor.Y))
                    Wake();
            };
        }

        public void Start()
        {
            BuildTray();
            BuildOverlays();
            ApplyStyle();
            _sound.Configure(_settings.Sound, _settings.Volume);
            RegisterHotkey();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            _topmostTimer.Start();
            _wakeTimer.Start();

            SetEnabled(_settings.StartEnabled);

            _tray.ShowBalloonTip(3000, "Ponteirinho está ativo",
                $"Use {HotkeyLabel} para mostrar/esconder a mãozinha. Opções no ícone perto do relógio.",
                WinForms.ToolTipIcon.None);
        }

        // ---------------- Loop de desenho ----------------

        /// <summary>Liga o desenho a cada quadro (60 fps). Ele se desliga sozinho quando tudo para.</summary>
        private void Wake()
        {
            _lastActivity = _clock.Elapsed.TotalSeconds;
            if (_renderLoopOn || !_enabled) return;
            CompositionTarget.Rendering += OnRendering;
            _renderLoopOn = true;
        }

        private void Sleep()
        {
            if (!_renderLoopOn) return;
            CompositionTarget.Rendering -= OnRendering;
            _renderLoopOn = false;
        }

        private void MarkDirty()
        {
            _dirty = true;
            Wake();
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!_enabled || !Native.GetCursorPos(out var cursor))
            {
                Sleep();
                return;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double since = now - _lastClick;
            bool inking = UpdateInkFade(now);
            bool animating = since < RippleDuration + 0.05 || inking;
            bool moved = cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y;
            if (moved || animating) _lastActivity = now;

            // Nada mudou: não redesenha, e depois de meio segundo parado o loop dorme.
            if (!moved && !animating && !_dirty)
            {
                if (now - _lastActivity > 0.5) Sleep();
                return;
            }
            _lastCursor = cursor;
            _dirty = false;

            foreach (var o in _overlays) o.UpdateInk(_strokes);

            var frame = new PointerFrame
            {
                CursorX = cursor.X,
                CursorY = cursor.Y,
                HandWidth = _settings.HandWidth,
                Side = _settings.Side,
                Press = since < PressDuration ? 1 - 0.14 * Math.Sin(Math.PI * since / PressDuration) : 1,
                Ripple = since < RippleDuration ? since / RippleDuration : -1,
            };

            foreach (var o in _overlays)
            {
                if (o.Contains(cursor.X, cursor.Y)) o.ShowPointer(frame);
                else o.HidePointer();
            }
        }

        /// <summary>Apaga aos poucos os riscos terminados. Devolve true enquanto houver risco na tela.</summary>
        private bool UpdateInkFade(double now)
        {
            for (int i = _strokes.Count - 1; i >= 0; i--)
            {
                var s = _strokes[i];
                if (s.EndedAt < 0) continue;
                double t = now - s.EndedAt - _settings.InkSeconds;
                if (t >= InkFadeDuration)
                {
                    _strokes.RemoveAt(i);
                    _dirty = true; // as janelas precisam tirar o risco
                }
                else if (t > 0)
                {
                    double f = t / InkFadeDuration;
                    s.Opacity = 1 - f * f; // começa devagar e acelera
                }
            }
            return _strokes.Count > 0;
        }

        // ---------------- Desenho ao arrastar ----------------

        /// <summary>
        /// Com o desenho ligado, o Ponteirinho segura o clique esquerdo: se o mouse andar, vira risco;
        /// se soltar sem andar, o clique é reenviado normalmente. Devolve true para engolir o evento.
        /// </summary>
        private bool HandleDrawing(int msg, Native.POINT pt)
        {
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    if (!_settings.DrawEnabled) return false;
                    _pressPending = true;
                    _pressPoint = pt;
                    return true;

                case Native.WM_MOUSEMOVE:
                    if (_pressPending &&
                        (Math.Abs(pt.X - _pressPoint.X) > DragThreshold || Math.Abs(pt.Y - _pressPoint.Y) > DragThreshold))
                    {
                        _pressPending = false;
                        _currentStroke = new InkStroke
                        {
                            Color = HandColors.Parse(_settings.InkColor),
                            Width = 5 * _settings.HandWidth / 72.0,
                        };
                        _currentStroke.Add(_pressPoint.X, _pressPoint.Y);
                        _strokes.Add(_currentStroke);
                    }
                    _currentStroke?.Add(pt.X, pt.Y);
                    return false; // o cursor precisa continuar andando

                case Native.WM_LBUTTONUP:
                    if (_pressPending)
                    {
                        // Foi só um clique: entrega para o programa embaixo.
                        _pressPending = false;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => Native.SendLeftClick(ReplayTag)));
                        return true;
                    }
                    if (_currentStroke != null)
                    {
                        _currentStroke.EndedAt = _clock.Elapsed.TotalSeconds;
                        _currentStroke = null;
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private void ToggleDraw()
        {
            _settings.DrawEnabled = !_settings.DrawEnabled;
            _settings.Save();
            if (!_settings.DrawEnabled) FinishStroke();
            if (_drawItem != null) _drawItem.Checked = _settings.DrawEnabled;
        }

        /// <summary>Termina o risco em andamento (ele some no tempo normal).</summary>
        private void FinishStroke()
        {
            if (_currentStroke != null) _currentStroke.EndedAt = _clock.Elapsed.TotalSeconds;
            _currentStroke = null;
            MarkDirty();
        }

        // ---------------- Janelas transparentes ----------------

        private void BuildOverlays()
        {
            foreach (var o in _overlays) o.Close();
            _overlays.Clear();

            foreach (var screen in WinForms.Screen.AllScreens)
            {
                var o = new OverlayWindow(screen.Bounds);
                o.HidePointer();
                _overlays.Add(o);
                if (_enabled) o.Show();
            }
            ApplyStyle();
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            // Monitor conectado/desconectado ou resolução mudou: refaz as janelas.
            Application.Current.Dispatcher.BeginInvoke(new Action(BuildOverlays));
        }

        /// <summary>Aplica o tipo e a cor atuais às janelas, ao ícone e às miniaturas do menu.</summary>
        private void ApplyStyle()
        {
            var style = HandStyle.Find(_settings.Style);
            var color = HandColors.Parse(_settings.Color);
            var visual = HandLibrary.Get(style, color);
            Color ripple = style.Kind switch
            {
                HandKind.Image => HandColors.Parse(style.Ink),
                HandKind.Laser => color,
                _ => Color.FromRgb((byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7)),
            };
            foreach (var o in _overlays) o.ApplyStyle(style, visual, color, _settings.WoodStick, ripple);

            UpdateTrayIcon(style, color);
            foreach (var item in _styleItems)
            {
                var s = (HandStyle)item.Tag;
                item.Checked = s == style;
                // Mãos pintáveis e o laser mostram a cor atual na miniatura.
                if (s.Kind != HandKind.Image || item.Image == null)
                {
                    var old = item.Image;
                    item.Image = HandLibrary.Thumbnail(s, color, 32);
                    old?.Dispose();
                }
            }
            foreach (var item in _colorItems)
                item.Checked = string.Equals((string)item.Tag, _settings.Color, StringComparison.OrdinalIgnoreCase);
            if (_colorMenu != null)
            {
                // As mãos do TinyHand já vêm coloridas.
                _colorMenu.Enabled = style.Kind != HandKind.Image;
                _colorMenu.ToolTipText = _colorMenu.Enabled ? null : "As mãos 3D já têm cor própria";
            }

            MarkDirty();
        }

        // ---------------- Liga / desliga ----------------

        private void SetEnabled(bool on)
        {
            _enabled = on;
            foreach (var o in _overlays)
            {
                if (on) o.Show();
                else o.Hide();
            }

            if (on) InstallMouseHook();
            else RemoveMouseHook();

            if (!on)
            {
                // Mão escondida: some com qualquer risco na hora.
                _pressPending = false;
                _currentStroke = null;
                _strokes.Clear();
                foreach (var o in _overlays) o.UpdateInk(_strokes);
            }

            if (on) MarkDirty();
            else Sleep();

            UpdateCursorVisibility();

            if (_toggleItem != null) _toggleItem.Checked = on;
            if (_tray != null) _tray.Text = on ? "Ponteirinho (ligado)" : "Ponteirinho (desligado)";
        }

        private void Toggle() => SetEnabled(!_enabled);

        /// <summary>O cursor some só enquanto a mão está aparecendo (e a opção está ligada).</summary>
        private void UpdateCursorVisibility()
        {
            if (_enabled && _settings.HideCursor)
            {
                if (!CursorHider.Hide())
                {
                    _tray?.ShowBalloonTip(4000, "Não deu para esconder o cursor",
                        "O Windows recusou a troca do cursor. A mãozinha continua funcionando normalmente.",
                        WinForms.ToolTipIcon.Warning);
                }
            }
            else if (CursorHider.IsHidden)
            {
                CursorHider.Restore();
            }
        }

        // ---------------- Clique do mouse (animação + som) ----------------

        private void InstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero) return;
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandle(null), 0);
        }

        private void RemoveMouseHook()
        {
            if (_mouseHook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Anota o clique (animação + som). Ele segue para os outros programas, exceto quando
            // o desenho ao arrastar está ligado (aí HandleDrawing decide).
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                bool replay = info.ExtraInfo == ReplayTag; // clique reenviado por nós: só passa

                if (!replay && (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN))
                {
                    _lastClick = _clock.Elapsed.TotalSeconds;
                    _sound.Play();
                }
                Wake(); // qualquer movimento/clique acorda o desenho

                try
                {
                    if (!replay && HandleDrawing(msg, info.Pt)) return new IntPtr(1);
                }
                catch
                {
                    // Um erro aqui nunca pode travar o mouse: deixa o evento passar.
                }
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // ---------------- Atalho global ----------------

        private void RegisterHotkey()
        {
            var p = new HwndSourceParameters("PonteirinhoMessages")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE: janela invisível só para mensagens
                Width = 0,
                Height = 0,
                WindowStyle = 0,
            };
            _messageWindow = new HwndSource(p);
            _messageWindow.AddHook(WndProc);

            bool ok = Native.RegisterHotKey(_messageWindow.Handle, HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)'H');
            if (!ok)
            {
                _tray?.ShowBalloonTip(4000, "Atalho indisponível",
                    $"Outro programa já usa {HotkeyLabel}. Use o ícone na bandeja para ligar/desligar.",
                    WinForms.ToolTipIcon.Warning);
            }

            bool drawOk = Native.RegisterHotKey(_messageWindow.Handle, DrawHotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)'D');
            if (!drawOk && _drawItem != null)
                _drawItem.Text = "Desenhar ao arrastar (atalho em uso por outro programa)";
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Toggle();
                handled = true;
            }
            else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == DrawHotkeyId)
            {
                ToggleDraw();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ---------------- Ícone na bandeja ----------------

        private void BuildTray()
        {
            var menu = new WinForms.ContextMenuStrip();

            _toggleItem = new WinForms.ToolStripMenuItem($"Mostrar mãozinha\t{HotkeyLabel}");
            _toggleItem.Click += (s, e) => Toggle();
            _toggleItem.Font = new Drawing.Font(_toggleItem.Font, Drawing.FontStyle.Bold);
            menu.Items.Add(_toggleItem);

            _drawItem = new WinForms.ToolStripMenuItem($"Desenhar ao arrastar\t{DrawHotkeyLabel}") { Checked = _settings.DrawEnabled };
            _drawItem.ToolTipText = "Clique e arraste para riscar a tela; o risco some sozinho. Clique simples continua funcionando.";
            _drawItem.Click += (s, e) => ToggleDraw();
            menu.Items.Add(_drawItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            // Tipo de mão
            WinForms.ToolStripMenuItem StyleItem(HandStyle style)
            {
                var item = new WinForms.ToolStripMenuItem(style.Label) { Tag = style, ImageScaling = WinForms.ToolStripItemImageScaling.None };
                item.Click += (s, e) =>
                {
                    _settings.Style = style.Id;
                    _settings.Save();
                    ApplyStyle();
                };
                _styleItems.Add(item);
                return item;
            }

            var styleMenu = new WinForms.ToolStripMenuItem("Tipo de mão");

            // As 36 mãos 3D num quadro de 3 colunas.
            var threeD = new WinForms.ToolStripMenuItem("Mãos 3D (TinyHand)");
            foreach (var style in HandStyle.TinyHand) threeD.DropDownItems.Add(StyleItem(style));
            var grid = threeD.DropDown;
            grid.LayoutStyle = WinForms.ToolStripLayoutStyle.Table;
            ((WinForms.TableLayoutSettings)grid.LayoutSettings).ColumnCount = 3;
            styleMenu.DropDownItems.Add(threeD);

            styleMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            styleMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem("Com cor à sua escolha:") { Enabled = false });
            foreach (var style in HandStyle.Paintable) styleMenu.DropDownItems.Add(StyleItem(style));
            styleMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            styleMenu.DropDownItems.Add(StyleItem(HandStyle.Laser));
            menu.Items.Add(styleMenu);

            // Cor (só para as mãos pintáveis e o laser)
            var colorMenu = new WinForms.ToolStripMenuItem("Cor");
            _colorMenu = colorMenu;
            foreach (var (name, hex) in HandColors.All)
            {
                var item = new WinForms.ToolStripMenuItem(name)
                {
                    Tag = hex,
                    Image = HandTinter.Swatch(HandColors.Parse(hex), 16),
                };
                item.Click += (s, e) =>
                {
                    _settings.Color = hex;
                    _settings.Save();
                    ApplyStyle();
                };
                _colorItems.Add(item);
                colorMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(colorMenu);

            // Tamanho
            var sizeMenu = new WinForms.ToolStripMenuItem("Tamanho");
            foreach (var (label, width) in Sizes)
            {
                var item = new WinForms.ToolStripMenuItem(label) { Checked = Math.Abs(_settings.HandWidth - width) < 0.5, Tag = width };
                item.Click += (s, e) =>
                {
                    _settings.HandWidth = width;
                    _settings.Save();
                    _sizeItems.ForEach(i => i.Checked = Math.Abs((double)i.Tag - width) < 0.5);
                    MarkDirty();
                };
                _sizeItems.Add(item);
                sizeMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(sizeMenu);

            // Lado da vareta
            var sideMenu = new WinForms.ToolStripMenuItem("Vareta");
            foreach (var (label, side) in Sides)
            {
                var item = new WinForms.ToolStripMenuItem(label) { Checked = _settings.Side == side, Tag = side };
                item.Click += (s, e) =>
                {
                    _settings.Side = side;
                    _settings.Save();
                    _sideItems.ForEach(i => i.Checked = (StickSide)i.Tag == side);
                    MarkDirty();
                };
                _sideItems.Add(item);
                sideMenu.DropDownItems.Add(item);
            }
            sideMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            var black = new WinForms.ToolStripMenuItem("Vareta preta") { Checked = !_settings.WoodStick };
            var wood = new WinForms.ToolStripMenuItem("Vareta de madeira") { Checked = _settings.WoodStick };
            void SetWood(bool w)
            {
                _settings.WoodStick = w;
                _settings.Save();
                black.Checked = !w;
                wood.Checked = w;
                ApplyStyle();
            }
            black.Click += (s, e) => SetWood(false);
            wood.Click += (s, e) => SetWood(true);
            sideMenu.DropDownItems.Add(black);
            sideMenu.DropDownItems.Add(wood);
            menu.Items.Add(sideMenu);

            // Som ao clicar
            var soundMenu = new WinForms.ToolStripMenuItem("Som ao clicar");
            foreach (var (header, label, kind) in SoundKinds)
            {
                if (header != null)
                {
                    soundMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
                    soundMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem(header) { Enabled = false });
                }
                var item = new WinForms.ToolStripMenuItem(label) { Checked = _settings.Sound == kind, Tag = kind };
                item.Click += (s, e) =>
                {
                    _settings.Sound = kind;
                    _settings.Save();
                    _soundItems.ForEach(i => i.Checked = (ClickSoundKind)i.Tag == kind);
                    _sound.Configure(kind, _settings.Volume);
                    _sound.Play(); // amostra
                };
                _soundItems.Add(item);
                soundMenu.DropDownItems.Add(item);
            }
            soundMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            for (int v = 0; v < VolumeLabels.Length; v++)
            {
                int level = v;
                var item = new WinForms.ToolStripMenuItem(VolumeLabels[v]) { Checked = _settings.Volume == v, Tag = v };
                item.Click += (s, e) =>
                {
                    _settings.Volume = level;
                    _settings.Save();
                    _volumeItems.ForEach(i => i.Checked = (int)i.Tag == level);
                    _sound.Configure(_settings.Sound, level);
                    _sound.Play(); // amostra
                };
                _volumeItems.Add(item);
                soundMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(soundMenu);

            // Desenho: cor e quanto tempo o risco fica
            var inkMenu = new WinForms.ToolStripMenuItem("Desenho (cor e tempo)");
            foreach (var (name, hex) in InkColors)
            {
                var item = new WinForms.ToolStripMenuItem(name)
                {
                    Tag = hex,
                    Image = HandTinter.Swatch(HandColors.Parse(hex), 16),
                    Checked = string.Equals(_settings.InkColor, hex, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (s, e) =>
                {
                    _settings.InkColor = hex;
                    _settings.Save();
                    _inkColorItems.ForEach(i => i.Checked = (string)i.Tag == hex);
                };
                _inkColorItems.Add(item);
                inkMenu.DropDownItems.Add(item);
            }
            inkMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            foreach (var (label, seconds) in InkDurations)
            {
                var item = new WinForms.ToolStripMenuItem(label) { Tag = seconds, Checked = Math.Abs(_settings.InkSeconds - seconds) < 0.01 };
                item.Click += (s, e) =>
                {
                    _settings.InkSeconds = seconds;
                    _settings.Save();
                    _inkTimeItems.ForEach(i => i.Checked = Math.Abs((double)i.Tag - seconds) < 0.01);
                };
                _inkTimeItems.Add(item);
                inkMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(inkMenu);

            var hideCursor = new WinForms.ToolStripMenuItem("Esconder o cursor do mouse") { Checked = _settings.HideCursor };
            hideCursor.Click += (s, e) =>
            {
                _settings.HideCursor = !_settings.HideCursor;
                hideCursor.Checked = _settings.HideCursor;
                _settings.Save();
                UpdateCursorVisibility();
            };
            menu.Items.Add(hideCursor);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var startEnabled = new WinForms.ToolStripMenuItem("Abrir já mostrando a mão") { Checked = _settings.StartEnabled };
            startEnabled.Click += (s, e) =>
            {
                _settings.StartEnabled = !_settings.StartEnabled;
                startEnabled.Checked = _settings.StartEnabled;
                _settings.Save();
            };
            menu.Items.Add(startEnabled);

            _startupItem = new WinForms.ToolStripMenuItem("Iniciar com o Windows") { Checked = IsInStartup() };
            _startupItem.Click += (s, e) =>
            {
                SetStartup(!IsInStartup());
                _startupItem.Checked = IsInStartup();
            };
            menu.Items.Add(_startupItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            var credits = new WinForms.ToolStripMenuItem("Créditos: mãos 3D do TinyHand (licença MIT)");
            credits.Click += (s, e) =>
            {
                try
                {
                    string license = System.IO.Path.Combine(AppContext.BaseDirectory, "LICENSE-TinyHand.txt");
                    Process.Start(new ProcessStartInfo(license) { UseShellExecute = true });
                }
                catch
                {
                    // Sem o arquivo, nada a mostrar.
                }
            };
            menu.Items.Add(credits);

            var quit = new WinForms.ToolStripMenuItem("Sair");
            quit.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(quit);

            _tray = new WinForms.NotifyIcon
            {
                ContextMenuStrip = menu,
                Text = "Ponteirinho",
                Visible = false,
            };
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) Toggle();
            };
        }

        /// <summary>O ícone da bandeja mostra a mão atual, na cor atual.</summary>
        private void UpdateTrayIcon(HandStyle style, Color color)
        {
            using var bmp = HandLibrary.Thumbnail(style, color, 64);
            IntPtr handle = bmp.GetHicon();
            var oldIcon = _tray.Icon;
            _tray.Icon = Drawing.Icon.FromHandle(handle);
            _tray.Visible = true;
            oldIcon?.Dispose();
            if (_trayIconHandle != IntPtr.Zero) Native.DestroyIcon(_trayIconHandle);
            _trayIconHandle = handle;
        }

        // ---------------- Iniciar com o Windows ----------------

        private static bool IsInStartup()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }

        private static void SetStartup(bool on)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunName, false);
        }

        // ---------------- Encerrar ----------------

        public void Dispose()
        {
            Sleep();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _topmostTimer.Stop();
            _wakeTimer.Stop();
            RemoveMouseHook();
            CursorHider.Restore();
            _sound.Dispose();

            if (_messageWindow != null)
            {
                Native.UnregisterHotKey(_messageWindow.Handle, HotkeyId);
                Native.UnregisterHotKey(_messageWindow.Handle, DrawHotkeyId);
                _messageWindow.Dispose();
            }

            if (_tray != null)
            {
                _tray.Visible = false; // evita "ícone fantasma" na bandeja
                _tray.Dispose();
            }
            if (_trayIconHandle != IntPtr.Zero) Native.DestroyIcon(_trayIconHandle);

            foreach (var o in _overlays) o.Close();
        }
    }
}
