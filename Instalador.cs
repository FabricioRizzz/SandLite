// SandLite-Instalador: um único .exe com o SandLite e o motor dentro.
// Copia tudo para Arquivos de Programas\SandLite e chama "SandLite.exe --pos-instalar",
// que instala o motor, cria os atalhos e registra o desinstalador no Windows.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SandLiteInstalador
{
    class Instalador : Form
    {
        static readonly string Dir = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files", "SandLite");
        Label info;
        CheckBox desktop;
        Button go;
        bool done, failed;

        Instalador()
        {
            Text = "Instalar SandLite";
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(420, 210);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var pic = new PictureBox { Left = 20, Top = 20, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom };
            try { pic.Image = new Icon(Icon, 64, 64).ToBitmap(); } catch { }
            Controls.Add(pic);
            Controls.Add(new Label { Text = "SandLite", Left = 100, Top = 22, AutoSize = true, Font = new Font("Segoe UI", 16f, FontStyle.Bold) });
            info = new Label { Text = "Rode uma segunda Steam (ou qualquer programa) isolada, ao mesmo tempo que a normal.\n\nSerá instalado em " + Dir, Left = 100, Top = 60, Width = 305, Height = 80 };
            Controls.Add(info);
            desktop = new CheckBox { Text = "Criar atalho na área de trabalho", Left = 20, Top = 166, Width = 240, Checked = true };
            Controls.Add(desktop);
            go = new Button { Text = "Instalar", Left = 300, Top = 160, Width = 104, Height = 34 };
            go.Click += delegate { if (failed) Close(); else if (done) { Open(); Close(); } else Install(); };
            Controls.Add(go);
            AcceptButton = go;
        }

        void Install()
        {
            go.Enabled = false; desktop.Enabled = false;
            info.Text = "Instalando...";
            Refresh();
            try
            {
                foreach (var p in Process.GetProcessesByName("SandLite")) try { p.Kill(); p.WaitForExit(3000); } catch { }

                // se o motor do SandLite já está rodando (atualização), os arquivos dele estão em uso: mantém
                var svc = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SbieSvc", "ImagePath", null) as string;
                var engineRunning = svc != null && svc.IndexOf(Dir, StringComparison.OrdinalIgnoreCase) >= 0;

                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.StartsWith("f:")) continue;
                    var rel = name.Substring(2).Replace('/', '\\');
                    if (engineRunning && rel.StartsWith("Motor\\", StringComparison.OrdinalIgnoreCase)) continue;
                    var dst = Path.Combine(Dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    using (var s = new System.IO.Compression.GZipStream(asm.GetManifestResourceStream(name), System.IO.Compression.CompressionMode.Decompress))
                    using (var o = File.Create(dst)) s.CopyTo(o);
                }

                var exe = Path.Combine(Dir, "SandLite.exe");
                var post = Process.Start(new ProcessStartInfo(exe, "--pos-instalar" + (desktop.Checked ? "" : " --sem-atalho")) { UseShellExecute = false });
                post.WaitForExit();
                if (post.ExitCode != 0)
                {
                    info.Text = "O motor de isolamento não instalou (código " + post.ExitCode + ").\nLog: C:\\ProgramData\\SandLite\\motor.log";
                    go.Text = "Fechar"; go.Enabled = true; failed = true;
                    return;
                }
                info.Text = "Pronto! O SandLite está instalado.\n\nPara remover depois: Configurações > Aplicativos > SandLite, ou o botão \"Desinstalar SandLite\" dentro do app.";
                go.Text = "Abrir SandLite"; go.Enabled = true; done = true;
            }
            catch (Exception ex)
            {
                info.Text = "Erro: " + ex.Message;
                go.Text = "Tentar de novo"; go.Enabled = true; desktop.Enabled = true;
            }
        }

        // abre pelo Explorer para o app não rodar como administrador
        void Open()
        {
            try { Process.Start("explorer.exe", "\"" + Path.Combine(Dir, "SandLite.exe") + "\""); } catch { }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new Instalador());
        }
    }
}
