using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Ponteirinho
{
    public partial class App : Application
    {
        private Mutex _singleInstance;
        private Controller _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Modos especiais (sem interface):
            //   --restaurar-cursor  traz o cursor de volta e sai (atalho de emergência)
            //   --vigia <pid>       espera o app principal fechar (mesmo à força) e restaura o cursor
            if (e.Args.Length >= 1 && e.Args[0] == "--restaurar-cursor")
            {
                CursorHider.Restore();
                Shutdown();
                return;
            }
            if (e.Args.Length >= 2 && e.Args[0] == "--vigia" && int.TryParse(e.Args[1], out int pid))
            {
                RunWatchdog(pid);
                return;
            }

            // Só uma cópia do app por vez.
            _singleInstance = new Mutex(true, "Ponteirinho.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            // Se uma execução anterior terminou mal, o cursor volta aqui.
            CursorHider.Restore();

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => CursorHider.Restore();
            AppDomain.CurrentDomain.ProcessExit += (s, a) => CursorHider.Restore();
            SessionEnding += (s, a) => CursorHider.Restore();

            // Janelas transparentes do tamanho da tela gastam bem menos memória
            // desenhadas pelo processador do que copiadas de volta da placa de vídeo.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            StartWatchdog();
            EnsureStartMenuShortcuts();

            _controller = new Controller();
            _controller.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _controller?.Dispose();
            CursorHider.Restore();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Abre uma cópia "vigia" do próprio app, que restaura o cursor quando este processo acabar.
        /// Ela é aberta via "cmd /c start" para ficar desgrudada: assim "Finalizar tarefa" no
        /// Gerenciador de Tarefas não fecha o app e o vigia juntos.
        /// </summary>
        private static void StartWatchdog()
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    $"/c start \"\" \"{Environment.ProcessPath}\" --vigia {Environment.ProcessId}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch
            {
                // Sem vigia, as outras proteções continuam valendo.
            }
        }

        /// <summary>
        /// Atalhos no menu Iniciar: "Ponteirinho" e "Restaurar cursor (Ponteirinho)".
        /// O segundo funciona só com o teclado: tecla Windows, digitar "Restaurar cursor", Enter.
        /// </summary>
        private static void EnsureStartMenuShortcuts()
        {
            try
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);

                void Make(string name, string args, string description)
                {
                    dynamic lnk = shell.CreateShortcut(Path.Combine(programs, name + ".lnk"));
                    lnk.TargetPath = Environment.ProcessPath;
                    lnk.Arguments = args;
                    lnk.Description = description;
                    lnk.IconLocation = Environment.ProcessPath + ",0";
                    lnk.WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
                    lnk.Save();
                }

                Make("Ponteirinho", "", "Mãozinha apontadora");
                Make("Restaurar cursor (Ponteirinho)", "--restaurar-cursor", "Traz o cursor do mouse de volta");
            }
            catch
            {
                // Sem atalhos, as outras proteções continuam valendo.
            }
        }

        private void RunWatchdog(int pid)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.WaitForExit();
                }
                catch
                {
                    // Processo já não existe.
                }
                CursorHider.Restore();
                Dispatcher.BeginInvoke(new Action(Shutdown));
            })
            { IsBackground = true, Name = "Vigia do cursor" };
            thread.Start();
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "erros.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
            }
            catch
            {
                // Se nem o log der para gravar, segue em frente.
            }
            e.Handled = true;
        }
    }
}
