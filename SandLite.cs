// SandLite - interface simples para o Sandboxie.
// O isolamento é feito pelo driver oficial (assinado) do Sandboxie; este app
// substitui só a interface: caixas, programas favoritos, atalhos e processos.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SandLite
{
    class Fav
    {
        public string Box, Name, Path, Args, Icon;
        public string Line() { return string.Join("\t", new[] { Box, Name, Path, Args ?? "", Icon ?? "" }); }
        public static Fav Parse(string l)
        {
            var p = l.Split('\t');
            if (p.Length < 3) return null;
            return new Fav { Box = p[0], Name = p[1], Path = p[2], Args = p.Length > 3 ? p[3] : "", Icon = p.Length > 4 ? p[4] : "" };
        }
    }

    class Game
    {
        public string AppId, Name, Dir;
        public override string ToString() { return Name; }
    }

    // ---------- acesso ao motor do Sandboxie ----------
    static class Sbie
    {
        public static string Dir, StartExe, IniExe, IniFile, SteamDir, SteamExe;

        public static void Find()
        {
            var dirs = new List<string>();
            var img = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SbieDrv", "ImagePath", null) as string;
            if (img != null) dirs.Add(System.IO.Path.GetDirectoryName(img.Replace(@"\??\", "").Trim('"')));
            var pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files";
            dirs.Add(System.IO.Path.Combine(pf, "Sandboxie-Plus"));
            dirs.Add(System.IO.Path.Combine(pf, "Sandboxie"));
            foreach (var d in dirs)
                if (d != null && File.Exists(System.IO.Path.Combine(d, "Start.exe")))
                {
                    Dir = d;
                    StartExe = System.IO.Path.Combine(d, "Start.exe");
                    IniExe = System.IO.Path.Combine(d, "SbieIni.exe");
                    break;
                }
            IniFile = new[] {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sandboxie.ini"),
                Dir == null ? null : System.IO.Path.Combine(Dir, "Sandboxie.ini")
            }.FirstOrDefault(p => p != null && File.Exists(p));

            var sp = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                  ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (sp != null)
            {
                sp = sp.Replace('/', '\\');
                if (File.Exists(System.IO.Path.Combine(sp, "steam.exe"))) { SteamDir = sp; SteamExe = System.IO.Path.Combine(sp, "steam.exe"); }
            }
        }

        public static List<string> Boxes()
        {
            var list = new List<string>();
            if (IniFile != null)
                foreach (Match m in Regex.Matches(File.ReadAllText(IniFile), @"^\[([^\]]+)\]", RegexOptions.Multiline))
                {
                    var n = m.Groups[1].Value.Trim();
                    if (n == "GlobalSettings" || n == "TemplateSettings" || n.StartsWith("UserSettings_") || n.StartsWith("Template_")) continue;
                    if (!list.Contains(n)) list.Add(n);
                }
            if (list.Count == 0) list.Add("DefaultBox");
            return list;
        }

        public static string Run(string args, bool wait)
        {
            var psi = new ProcessStartInfo(StartExe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = wait };
            var p = Process.Start(psi);
            if (!wait) return null;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }

        public static string LaunchArgs(string box, string path, string args)
        {
            var a = "/box:" + box + " \"" + path + "\"";
            if (!string.IsNullOrEmpty(args)) a += " " + args;
            return a;
        }

        public static List<int> Pids(string box)
        {
            var r = new List<int>();
            try
            {
                var lines = Run("/box:" + box + " /listpids", true).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines.Skip(1)) { int pid; if (int.TryParse(l.Trim(), out pid)) r.Add(pid); }
            }
            catch { }
            return r;
        }

        public static void Set(string box, string key, string value)
        {
            var p = Process.Start(new ProcessStartInfo(IniExe, "set " + box + " " + key + " \"" + value + "\"") { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit(5000);
        }

        public static string BoxFolder(string box)
        {
            string root = null;
            try
            {
                var p = Process.Start(new ProcessStartInfo(IniExe, "query " + box + " FileRootPath") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                root = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
            }
            catch { }
            if (string.IsNullOrEmpty(root)) root = @"%SystemDrive%\Sandbox\%USER%\%SANDBOX%";
            root = root.Replace(@"\??\", "").Replace("%SANDBOX%", box).Replace("%USER%", Environment.UserName);
            return Environment.ExpandEnvironmentVariables(root);
        }

        public static List<Game> SteamGames()
        {
            var games = new List<Game>();
            if (SteamDir == null) return games;
            var libs = new List<string> { SteamDir };
            var vdf = System.IO.Path.Combine(SteamDir, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            var seen = new HashSet<string>();
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = System.IO.Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var f in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var t = File.ReadAllText(f);
                        var id = Regex.Match(t, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                        var name = Regex.Match(t, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        if (id == "" || name.StartsWith("Steamworks") || name.StartsWith("Proton") || !seen.Add(id)) continue;
                        var dir = Regex.Match(t, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        games.Add(new Game { AppId = id, Name = name, Dir = dir == "" ? null : System.IO.Path.Combine(apps, "common", dir) });
                    }
                    catch { }
                }
            }
            games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return games;
        }

        // ícone do jogo: o que a Steam registra no Windows; se não houver, o .exe do jogo
        public static string GameIcon(Game g)
        {
            foreach (var root in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App ",
                                         @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " })
            {
                var icon = Registry.GetValue(root + g.AppId, "DisplayIcon", null) as string;
                if (icon != null && File.Exists(icon.Trim('"'))) return icon.Trim('"');
            }
            try
            {
                if (g.Dir != null && Directory.Exists(g.Dir))
                    return Directory.GetFiles(g.Dir, "*.exe", SearchOption.AllDirectories)
                        .Where(f => !Regex.IsMatch(System.IO.Path.GetFileName(f), "crash|unins|setup|redist|launcher|helper|report|vc_|dxsetup|eac|battleye", RegexOptions.IgnoreCase))
                        .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch { }
            return null;
        }
    }

    // ---------- instalação do motor que vem na pasta "motor" ----------
    // Mesmos passos do instalador oficial: copia os arquivos, registra o driver
    // (minifiltro, altitude 86900) e o serviço com o KmdUtil e inicia o serviço.
    static class Engine
    {
        public static readonly string Target = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files", @"SandLite\Motor");
        public static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"SandLite\motor.log");
        public static readonly string SandboxRoot = Environment.ExpandEnvironmentVariables(@"%SystemDrive%\SandLite");
        static string Package { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "motor"); } }

        public static bool PackageAvailable() { return File.Exists(Path.Combine(Package, "SbieDrv.sys")); }
        public static bool IsOurs() { return Sbie.Dir != null && string.Equals(Sbie.Dir.TrimEnd('\\'), Target, StringComparison.OrdinalIgnoreCase); }

        static void Log(string s)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)); File.AppendAllText(LogFile, DateTime.Now.ToString("s") + "  " + s + "\r\n"); } catch { }
        }

        static int Kmd(string args)
        {
            var p = Process.Start(new ProcessStartInfo(Path.Combine(Target, "KmdUtil.exe"), args) { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit(30000);
            Log("KmdUtil " + args + "  -> " + p.ExitCode);
            return p.ExitCode;
        }

        static bool ServiceExists(string name)
        {
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + name, "ImagePath", null) != null;
        }

        public static int Install()
        {
            try
            {
                Log("Instalando motor em " + Target);
                if (ServiceExists("SbieSvc") || ServiceExists("SbieDrv")) { Log("Já existe um motor instalado; nada a fazer."); return 0; }
                if (!PackageAvailable()) { Log("Pasta motor não encontrada: " + Package); return 3; }

                // instalado pelo instalador: os arquivos já estão no lugar certo
                var same = string.Equals(Path.GetFullPath(Package).TrimEnd('\\'), Target, StringComparison.OrdinalIgnoreCase);
                if (!same) foreach (var src in Directory.GetFiles(Package, "*", SearchOption.AllDirectories))
                {
                    var dst = Path.Combine(Target, src.Substring(Package.Length).TrimStart('\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                }

                var ini = Path.Combine(Target, "Sandboxie.ini");
                if (!File.Exists(ini))
                    File.WriteAllText(ini, string.Join("\r\n", new[] {
                        "[GlobalSettings]",
                        @"FileRootPath=\??\%SystemDrive%\SandLite\%USER%\%SANDBOX%",
                        "",
                        "[STEAM]",
                        "Enabled=y",
                        "ConfigLevel=10",
                        "BlockNetworkFiles=y",
                        "Template=AutoRecoverIgnore",
                        "Template=LingerPrograms",
                        "Template=BlockPorts",
                        "Template=qWave",
                        "Template=FileCopy",
                        "Template=SkipHook",
                        "Template=OpenBluetooth",
                        "Template=OpenSmartCard",
                        "" }), System.Text.Encoding.Unicode);

                var drv = Path.Combine(Target, "SbieDrv.sys");
                var svc = Path.Combine(Target, "SbieSvc.exe");
                var msg = Path.Combine(Target, "SbieMsg.dll");
                if (Kmd("install SbieDrv \"" + drv + "\" type=kernel start=demand msgfile=\"" + msg + "\" altitude=86900") != 0) return 4;
                if (Kmd("install SbieSvc \"" + svc + "\" type=own start=auto msgfile=\"" + msg + "\" display=\"SandLite\" group=UIGroup") != 0) return 5;
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SbieSvc", "PreferExternalManifest", 1, RegistryValueKind.DWord);
                if (Kmd("start SbieSvc") != 0) return 6;
                Log("Motor instalado.");
                return 0;
            }
            catch (Exception ex) { Log("Erro: " + ex); return 1; }
        }

        public static int Remove()
        {
            try
            {
                Log("Removendo motor");
                var start = Path.Combine(Target, "Start.exe");
                if (File.Exists(start)) Process.Start(new ProcessStartInfo(start, "/terminate_all") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(15000);
                Kmd("stop SbieSvc");
                Kmd("stop SbieDrv");
                Kmd("delete SbieSvc");
                Kmd("delete SbieDrv");
                try { Directory.Delete(Target, true); }
                catch (Exception ex) { Log("Alguns arquivos ficaram (serão liberados depois de reiniciar): " + ex.Message); }
                Log("Motor removido.");
                return 0;
            }
            catch (Exception ex) { Log("Erro: " + ex); return 1; }
        }
    }

    // ---------- instalação completa (usada pelo SandLite-Instalador.exe) ----------
    static class Setup
    {
        public static readonly string InstallDir = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files", "SandLite");
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SandLite";
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SandLite.lnk"); } }
        static string MenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SandLite.lnk"); } }

        public static bool IsInstalled()
        {
            return string.Equals(Path.GetDirectoryName(Application.ExecutablePath).TrimEnd('\\'), InstallDir, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsAdmin()
        {
            return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        static void Shortcut(string lnk, string target)
        {
            var sc = MainForm.ShellShortcut(lnk);
            MainForm.Set(sc, "TargetPath", target);
            MainForm.Set(sc, "WorkingDirectory", Path.GetDirectoryName(target));
            MainForm.Set(sc, "IconLocation", target + ",0");
            MainForm.Set(sc, "Description", "SandLite - rode programas e Steams isoladas");
            sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        // chamado pelo instalador depois de copiar os arquivos (já como administrador)
        public static int PostInstall(bool desktop)
        {
            var code = Engine.Install();
            if (code != 0) return code;
            var exe = Path.Combine(InstallDir, "SandLite.exe");
            try
            {
                Shortcut(MenuLnk, exe);
                if (desktop) Shortcut(DesktopLnk, exe);
                using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "SandLite");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("DisplayVersion", "1.0");
                    k.SetValue("Publisher", "SandLite");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + exe + "\" --desinstalar");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { MessageBox.Show("Instalado, mas não consegui criar os atalhos: " + ex.Message, "SandLite"); }
            return 0;
        }

        public static int Uninstall()
        {
            if (!IsAdmin())
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--desinstalar") { UseShellExecute = true, Verb = "runas" });
                    p.WaitForExit();
                    return p.ExitCode;
                }
                catch { return 1; }
            }

            var f = new Form { Text = "Desinstalar SandLite", ClientSize = new Size(380, 150), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 9.5f) };
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            f.Controls.Add(new Label { Text = "Remover o SandLite, o motor de isolamento e os atalhos de jogos isolados deste PC?", Left = 14, Top = 14, Width = 352, Height = 40 });
            var data = new CheckBox { Text = "Apagar também os dados das caixas (logins das Steams isoladas)", Left = 14, Top = 60, Width = 352, Height = 36 };
            var yes = new Button { Text = "Desinstalar", Left = 176, Top = 108, Width = 100, Height = 30, DialogResult = DialogResult.OK };
            var no = new Button { Text = "Cancelar", Left = 282, Top = 108, Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { data, yes, no });
            f.AcceptButton = yes; f.CancelButton = no;
            if (f.ShowDialog() != DialogResult.OK) return 2;

            var me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("SandLite")) if (p.Id != me) try { p.Kill(); p.WaitForExit(3000); } catch { }

            Sbie.Find();
            var startExe = Path.Combine(Engine.Target, "Start.exe");
            if (Engine.IsOurs()) Engine.Remove();   // nunca mexe num Sandboxie instalado separadamente

            // atalhos: o do app e os de jogos/programas isolados que apontam para o nosso motor
            foreach (var lnk in new[] { DesktopLnk, MenuLnk }) try { File.Delete(lnk); } catch { }
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                foreach (var lnk in Directory.GetFiles(dir, "*.lnk"))
                    try { if (string.Equals((string)MainForm.Get(MainForm.ShellShortcut(lnk), "TargetPath"), startExe, StringComparison.OrdinalIgnoreCase)) File.Delete(lnk); } catch { }

            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (data.Checked)
            {
                try { Directory.Delete(Engine.SandboxRoot, true); } catch { }
                try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SandLite"), true); } catch { }
            }

            MessageBox.Show("O SandLite foi removido.", "SandLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
            // apaga a pasta do programa depois que este processo fechar
            if (IsInstalled())
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"") { UseShellExecute = false, CreateNoWindow = true });
            return 0;
        }
    }

    // ---------- janela principal ----------
    class MainForm : Form
    {
        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SandLite");
        static readonly string FavFile = Path.Combine(DataDir, "favoritos.txt");

        List<Fav> favs = new List<Fav>();
        ListView boxList, favList, procList;
        ImageList favIcons = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
        ImageList boxIcons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        ToolStripStatusLabel status;
        NotifyIcon tray;
        Timer timer;
        bool reallyClose;

        public MainForm()
        {
            Sbie.Find();
            Text = "SandLite";
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(820, 540);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = MakeAppIcon(); }

            // barra de ferramentas
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2), RenderMode = ToolStripRenderMode.System };
            tools.Items.Add(Btn("+ Programa", "Adicionar um programa à caixa selecionada", delegate { AddProgram(); }));
            tools.Items.Add(Btn("+ Jogo Steam", "Adicionar um jogo da Steam à caixa selecionada", delegate { AddSteamGame(); }));
            tools.Items.Add(Btn("Executar...", "Rodar um programa uma vez, sem salvar", delegate { RunOnce(); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("Nova caixa", "Criar uma nova caixa isolada", delegate { CreateBox(); }));
            tools.Items.Add(Btn("Pasta da caixa", "Abrir os arquivos guardados pela caixa", delegate { OpenBoxFolder(); }));
            tools.Items.Add(Btn("Esvaziar caixa", "Apagar tudo que a caixa guardou (logins, saves isolados...)", delegate { EmptyBox(); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("Fechar tudo", "Fechar todos os programas da caixa selecionada", delegate { TerminateBox(); }));
            var about = Btn("Sobre", "Créditos e licença", delegate { About(); });
            about.Alignment = ToolStripItemAlignment.Right;
            tools.Items.Add(about);
            if (Setup.IsInstalled())
            {
                var rm = Btn("Desinstalar SandLite", "Remover o SandLite e o motor deste PC", delegate { Elevated("--desinstalar"); });
                rm.Alignment = ToolStripItemAlignment.Right;
                tools.Items.Add(rm);
            }
            else if (Engine.IsOurs())
            {
                var rm = Btn("Desinstalar motor", "Remover o motor de isolamento do SandLite deste PC", delegate { RemoveEngine(); });
                rm.Alignment = ToolStripItemAlignment.Right;
                tools.Items.Add(rm);
            }

            // caixas
            boxList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, SmallImageList = boxIcons };
            boxList.Columns.Add("Caixa", 125);
            boxList.Columns.Add("Rodando", 70);
            boxList.SelectedIndexChanged += delegate { FillFavs(); RefreshProcs(); };

            // favoritos
            favList = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = favIcons, MultiSelect = false, HideSelection = false };
            favList.DoubleClick += delegate { LaunchSelected(); };
            favList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) LaunchSelected(); if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            var favMenu = new ContextMenuStrip();
            favMenu.Items.Add("Abrir", null, delegate { LaunchSelected(); });
            favMenu.Items.Add("Criar atalho na área de trabalho", null, delegate { var f = SelectedFav(); if (f != null) MakeShortcut(f); });
            favMenu.Items.Add("Renomear", null, delegate { RenameSelected(); });
            favMenu.Items.Add(new ToolStripSeparator());
            favMenu.Items.Add("Remover da lista", null, delegate { RemoveSelected(); });
            favMenu.Opening += (s, e) => { if (SelectedFav() == null) e.Cancel = true; };
            favList.ContextMenuStrip = favMenu;

            var favButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 4, 0, 0) };
            favButtons.Controls.Add(Big("Abrir", delegate { LaunchSelected(); }));
            favButtons.Controls.Add(Big("Criar atalho", delegate { var f = SelectedFav(); if (f != null) MakeShortcut(f); }));
            favButtons.Controls.Add(Big("Remover", delegate { RemoveSelected(); }));

            var favPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            favPanel.Controls.Add(favList);
            favPanel.Controls.Add(favButtons);
            favPanel.Controls.Add(new Label { Text = "Programas da caixa  (arraste um .exe ou atalho para cá)", Dock = DockStyle.Top, Height = 22, ForeColor = Color.DimGray });

            // processos
            procList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            procList.Columns.Add("Programa rodando isolado", 300);
            procList.Columns.Add("PID", 80);
            var killBtn = Big("Fechar selecionado", delegate { KillSelected(); });
            killBtn.Width = 150;
            var procButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 4, 0, 0) };
            procButtons.Controls.Add(killBtn);
            var procPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 8) };
            procPanel.Controls.Add(procList);
            procPanel.Controls.Add(procButtons);

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
            right.Panel1.Controls.Add(favPanel);
            right.Panel2.Controls.Add(procPanel);

            var main = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 200 };
            var boxPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 0, 8) };
            boxPanel.Controls.Add(boxList);
            main.Panel1.Controls.Add(boxPanel);
            main.Panel2.Controls.Add(right);

            var bar = new StatusStrip();
            status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            bar.Items.Add(status);

            Controls.Add(main);
            Controls.Add(tools);
            Controls.Add(bar);

            // bandeja
            tray = new NotifyIcon { Icon = Icon, Text = "SandLite", Visible = true };
            tray.DoubleClick += delegate { ShowMain(); };
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildTrayMenu(); };

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop)) AddPath(f); };
            favList.AllowDrop = true;
            favList.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            favList.DragDrop += (s, e) => { foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop)) AddPath(f); };

            FormClosing += (s, e) =>
            {
                if (!reallyClose && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    tray.ShowBalloonTip(2000, "SandLite", "Continuo aqui na bandeja. Clique com o botão direito para abrir seus programas.", ToolTipIcon.Info);
                }
            };
            FormClosed += delegate { tray.Visible = false; };

            timer = new Timer { Interval = 3000 };
            timer.Tick += delegate { if (Visible) RefreshProcs(); };
            timer.Start();

            Load += delegate { main.SplitterDistance = 210; right.SplitterDistance = (int)(right.Height * 0.58); };

            LoadFavs();
            LoadBoxes();

            Shown += delegate { if (Sbie.StartExe == null) OfferEngineInstall(); };
            if (Sbie.StartExe == null) Status("O motor de isolamento ainda não está instalado.");
            else Status("Pronto. Dê dois cliques num programa para abri-lo isolado.");
        }

        ToolStripButton Btn(string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, Padding = new Padding(4, 2, 4, 2) };
            b.Click += click;
            return b;
        }

        Button Big(string text, EventHandler click)
        {
            var b = new Button { Text = text, Width = 120, Height = 30 };
            b.Click += click;
            return b;
        }

        void Status(string s) { status.Text = s; }

        static Icon MakeAppIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(0, 150, 170))) g.FillRectangle(b, 4, 10, 24, 18);
                using (var b = new SolidBrush(Color.FromArgb(0, 200, 220))) g.FillPolygon(b, new[] { new Point(2, 10), new Point(16, 3), new Point(30, 10), new Point(16, 15) });
                using (var p = new Pen(Color.White, 2)) g.DrawLine(p, 16, 15, 16, 28);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        string Box() { return boxList.SelectedItems.Count == 0 ? null : boxList.SelectedItems[0].Text; }

        bool Ready()
        {
            if (Sbie.StartExe == null) { MessageBox.Show(this, "Motor do Sandboxie não encontrado.", Text); return false; }
            if (Box() == null) { MessageBox.Show(this, "Escolha uma caixa à esquerda.", Text); return false; }
            return true;
        }

        void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        // ---------- caixas ----------

        void LoadBoxes()
        {
            var sel = Box();
            boxList.Items.Clear();
            boxIcons.Images.Clear();
            boxIcons.Images.Add(Square(Color.FromArgb(0, 180, 200)));
            foreach (var b in Sbie.Boxes())
                boxList.Items.Add(new ListViewItem(new[] { b, "" }, 0));
            var pick = boxList.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text == sel)
                    ?? boxList.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text == "STEAM")
                    ?? (boxList.Items.Count > 0 ? boxList.Items[0] : null);
            if (pick != null) pick.Selected = true;
        }

        static Bitmap Square(Color c)
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b)) using (var br = new SolidBrush(c)) g.FillRectangle(br, 3, 3, 10, 10);
            return b;
        }

        void CreateBox()
        {
            if (Sbie.IniExe == null || !File.Exists(Sbie.IniExe)) { MessageBox.Show(this, "SbieIni.exe não encontrado.", Text); return; }
            var name = Prompt("Nome da nova caixa (letras, números e _):", "STEAM2");
            if (name == null) return;
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{1,32}$")) { MessageBox.Show(this, "Nome inválido.", Text); return; }
            if (!Sbie.Boxes().Contains(name))
            {
                try
                {
                    Sbie.Set(name, "Enabled", "y");
                    Sbie.Set(name, "ConfigLevel", "10");
                    Sbie.Set(name, "BlockNetworkFiles", "y");
                    Sbie.Set(name, "BorderColor", "#00FFFF,ttl,6,192");
                    Sbie.Run("/reload", false);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
                System.Threading.Thread.Sleep(500);
            }
            LoadBoxes();
            foreach (ListViewItem i in boxList.Items) i.Selected = i.Text == name;
            Status("Caixa " + name + " pronta.");
        }

        void OpenBoxFolder()
        {
            if (!Ready()) return;
            var dir = Sbie.BoxFolder(Box());
            if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
            else MessageBox.Show(this, "A caixa ainda não guardou nada.\n" + dir, Text);
        }

        void EmptyBox()
        {
            if (!Ready()) return;
            if (MessageBox.Show(this, "Apagar tudo que a caixa " + Box() + " guardou?\n\nIsso fecha os programas dela e apaga logins e arquivos criados lá dentro (por exemplo, o login da Steam isolada). Os seus arquivos normais não são tocados.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Sbie.Run("/box:" + Box() + " /terminate", true);
            Sbie.Run("/box:" + Box() + " delete_sandbox_silent", false);
            Status("Caixa " + Box() + " esvaziada.");
        }

        void TerminateBox()
        {
            if (!Ready()) return;
            Sbie.Run("/box:" + Box() + " /terminate", true);
            Status("Programas da caixa " + Box() + " fechados.");
            RefreshProcs();
        }

        // ---------- processos ----------

        void RefreshProcs()
        {
            var box = Box();
            if (box == null || Sbie.StartExe == null) return;
            var pids = Sbie.Pids(box);
            var item = boxList.SelectedItems.Count > 0 ? boxList.SelectedItems[0] : null;
            if (item != null) item.SubItems[1].Text = pids.Count == 0 ? "" : pids.Count.ToString();

            var selected = new HashSet<string>(procList.SelectedItems.Cast<ListViewItem>().Select(i => i.SubItems[1].Text));
            procList.BeginUpdate();
            procList.Items.Clear();
            foreach (var pid in pids)
            {
                string name;
                try { name = Process.GetProcessById(pid).ProcessName + ".exe"; } catch { continue; }
                if (name.StartsWith("Sandboxie", StringComparison.OrdinalIgnoreCase)) continue;   // processos internos do motor
                var li = new ListViewItem(new[] { name, pid.ToString() });
                li.Selected = selected.Contains(li.SubItems[1].Text);
                procList.Items.Add(li);
            }
            procList.EndUpdate();
        }

        void KillSelected()
        {
            foreach (ListViewItem i in procList.SelectedItems)
                try { Process.GetProcessById(int.Parse(i.SubItems[1].Text)).Kill(); } catch { }
            RefreshProcs();
        }

        // ---------- favoritos ----------

        void LoadFavs()
        {
            favs.Clear();
            try { foreach (var l in File.ReadAllLines(FavFile)) { var f = Fav.Parse(l); if (f != null) favs.Add(f); } }
            catch { }
            if (!File.Exists(FavFile) && Sbie.SteamExe != null && Sbie.Boxes().Contains("STEAM"))
            {
                favs.Add(new Fav { Box = "STEAM", Name = "Steam", Path = Sbie.SteamExe });
                SaveFavs();
            }
        }

        void SaveFavs()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllLines(FavFile, favs.Select(f => f.Line()));
            }
            catch (Exception ex) { Status("Não consegui salvar a lista: " + ex.Message); }
        }

        void FillFavs()
        {
            var box = Box();
            favList.BeginUpdate();
            favList.Items.Clear();
            favIcons.Images.Clear();
            foreach (var f in favs.Where(f => f.Box == box))
            {
                favIcons.Images.Add(IconFor(f));
                favList.Items.Add(new ListViewItem(f.Name, favIcons.Images.Count - 1) { Tag = f, ToolTipText = f.Path + " " + f.Args });
            }
            favList.EndUpdate();
        }

        static Bitmap IconFor(Fav f)
        {
            foreach (var p in new[] { f.Icon, f.Path })
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                try
                {
                    var ico = Icon.ExtractAssociatedIcon(p);
                    if (ico != null) return new Icon(ico, 32, 32).ToBitmap();
                }
                catch { }
            }
            return SystemIcons.Application.ToBitmap();
        }

        Fav SelectedFav() { return favList.SelectedItems.Count == 0 ? null : favList.SelectedItems[0].Tag as Fav; }

        void AddPath(string file)
        {
            if (!Ready()) return;
            var path = file;
            var args = "";
            var icon = "";
            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sc = ShellShortcut(file);
                    var t = (string)Get(sc, "TargetPath");
                    // atalho que já aponta pro Start.exe do Sandboxie: aproveita o programa de dentro
                    var a = (string)Get(sc, "Arguments");
                    if (t.EndsWith("Start.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(a, "^(?:/\\S+\\s+)*\"([^\"]+)\"\\s*(.*)$");
                        if (m.Success) { t = m.Groups[1].Value; a = m.Groups[2].Value; }
                    }
                    if (!string.IsNullOrEmpty(t)) { path = t; args = a; }
                    var il = ((string)Get(sc, "IconLocation") ?? "").Split(',')[0];
                    if (File.Exists(il)) icon = il;
                }
                catch { }
            }
            else if (file.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                // atalho de jogo da Steam (steam://rungameid/ID)
                var txt = File.ReadAllText(file);
                var id = Regex.Match(txt, @"steam://rungameid/(\d+)").Groups[1].Value;
                if (id != "" && Sbie.SteamExe != null)
                {
                    path = Sbie.SteamExe;
                    args = "-applaunch " + id;
                    var im = Regex.Match(txt, @"IconFile=(.+)").Groups[1].Value.Trim();
                    if (File.Exists(im)) icon = im;
                }
            }
            if (!File.Exists(path)) { Status("Não achei o programa: " + path); return; }
            var name = Path.GetFileNameWithoutExtension(file);
            AddFav(new Fav { Box = Box(), Name = name, Path = path, Args = args, Icon = icon });
        }

        void AddFav(Fav f)
        {
            favs.Add(f);
            SaveFavs();
            FillFavs();
            foreach (ListViewItem i in favList.Items) i.Selected = i.Tag == f;
            Status(f.Name + " adicionado à caixa " + f.Box + ".");
        }

        void AddProgram()
        {
            if (!Ready()) return;
            using (var d = new OpenFileDialog { Title = "Escolha o programa", Filter = "Programas e atalhos|*.exe;*.lnk;*.url;*.bat|Todos|*.*", DereferenceLinks = false })
                if (d.ShowDialog(this) == DialogResult.OK) AddPath(d.FileName);
        }

        void AddSteamGame()
        {
            if (!Ready()) return;
            if (Sbie.SteamExe == null) { MessageBox.Show(this, "Steam não encontrada.", Text); return; }
            var games = Sbie.SteamGames();
            var f = new Form { Text = "Adicionar jogo da Steam", ClientSize = new Size(360, 380), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font };
            var filter = new TextBox { Left = 12, Top = 12, Width = 336 };
            var list = new ListBox { Left = 12, Top = 42, Width = 336, Height = 286 };
            Action fill = () => { list.Items.Clear(); foreach (var g in games) if (g.Name.IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0) list.Items.Add(g); };
            filter.TextChanged += delegate { fill(); };
            fill();
            var ok = new Button { Text = "Adicionar", Left = 248, Top = 338, Width = 100, Height = 30, DialogResult = DialogResult.OK };
            var desk = new CheckBox { Text = "Criar atalho na área de trabalho", Left = 12, Top = 343, Width = 230, Checked = true };
            list.DoubleClick += delegate { if (list.SelectedItem != null) { f.DialogResult = DialogResult.OK; } };
            f.Controls.AddRange(new Control[] { filter, list, desk, ok });
            f.AcceptButton = ok;
            if (f.ShowDialog(this) != DialogResult.OK) return;
            var game = list.SelectedItem as Game;
            if (game == null) return;
            var fav = new Fav { Box = Box(), Name = game.Name, Path = Sbie.SteamExe, Args = "-applaunch " + game.AppId, Icon = Sbie.GameIcon(game) ?? "" };
            AddFav(fav);
            if (desk.Checked) MakeShortcut(fav);
        }

        void RunOnce()
        {
            if (!Ready()) return;
            using (var d = new OpenFileDialog { Title = "Rodar isolado na caixa " + Box(), Filter = "Programas|*.exe;*.bat;*.cmd|Todos|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) Launch(new Fav { Box = Box(), Name = Path.GetFileName(d.FileName), Path = d.FileName });
        }

        void RenameSelected()
        {
            var f = SelectedFav();
            if (f == null) return;
            var n = Prompt("Novo nome:", f.Name);
            if (string.IsNullOrEmpty(n)) return;
            f.Name = n;
            SaveFavs();
            FillFavs();
        }

        void RemoveSelected()
        {
            var f = SelectedFav();
            if (f == null) return;
            favs.Remove(f);
            SaveFavs();
            FillFavs();
        }

        void LaunchSelected() { var f = SelectedFav(); if (f != null) Launch(f); }

        void Launch(Fav f)
        {
            if (Sbie.StartExe == null) return;
            try
            {
                Sbie.Run(Sbie.LaunchArgs(f.Box, f.Path, f.Args), false);
                Status("Abrindo " + f.Name + " na caixa " + f.Box + "...");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        }

        // ---------- atalhos ----------

        internal static object ShellShortcut(string lnk)
        {
            var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        }
        internal static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }
        internal static void Set(object o, string p, object v) { o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v }); }

        void MakeShortcut(Fav f)
        {
            var title = f.Name + " (" + f.Box + ")";
            var safe = string.Concat(title.Split(Path.GetInvalidFileNameChars()));
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), safe + ".lnk");
            try
            {
                var sc = ShellShortcut(lnk);
                Set(sc, "TargetPath", Sbie.StartExe);
                Set(sc, "Arguments", Sbie.LaunchArgs(f.Box, f.Path, f.Args));
                Set(sc, "WorkingDirectory", Path.GetDirectoryName(f.Path));
                Set(sc, "IconLocation", (string.IsNullOrEmpty(f.Icon) ? f.Path : f.Icon) + ",0");
                Set(sc, "Description", "Abre " + f.Name + " isolado na caixa " + f.Box);
                sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Status("Atalho criado na área de trabalho: " + safe);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não consegui criar o atalho: " + ex.Message, Text); }
        }

        // ---------- bandeja ----------

        void BuildTrayMenu()
        {
            var m = tray.ContextMenuStrip;
            m.Items.Clear();
            foreach (var box in favs.Select(f => f.Box).Distinct())
            {
                var sub = new ToolStripMenuItem(box);
                foreach (var f in favs.Where(x => x.Box == box))
                {
                    var fav = f;
                    Image img = null;
                    try { img = IconFor(fav); } catch { }
                    sub.DropDownItems.Add(fav.Name, img, delegate { Launch(fav); });
                }
                sub.DropDownItems.Add(new ToolStripSeparator());
                var b = box;
                sub.DropDownItems.Add("Fechar tudo nesta caixa", null, delegate { Sbie.Run("/box:" + b + " /terminate", false); });
                m.Items.Add(sub);
            }
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Abrir SandLite", null, delegate { ShowMain(); });
            m.Items.Add("Sair", null, delegate { reallyClose = true; Close(); });
        }

        string Prompt(string label, string def)
        {
            var f = new Form { Text = Text, ClientSize = new Size(320, 100), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font };
            var t = new TextBox { Left = 12, Top = 34, Width = 296, Text = def };
            var ok = new Button { Text = "OK", Left = 228, Top = 64, Width = 80, DialogResult = DialogResult.OK };
            f.Controls.Add(new Label { Text = label, Left = 12, Top = 10, AutoSize = true });
            f.Controls.Add(t); f.Controls.Add(ok); f.AcceptButton = ok;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
        }

        void About()
        {
            var f = new Form { Text = "Sobre o SandLite", ClientSize = new Size(420, 262), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, Icon = Icon };
            var pic = new PictureBox { Left = 16, Top = 16, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom };
            try { pic.Image = new Icon(Icon, 64, 64).ToBitmap(); } catch { }
            f.Controls.Add(pic);
            f.Controls.Add(new Label { Text = "SandLite 1.0", Left = 96, Top = 18, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
            f.Controls.Add(new Label { Text = "Criado por Fabricio Riz", Left = 98, Top = 52, AutoSize = true });
            f.Controls.Add(new Label
            {
                Text = "O isolamento é feito pelo motor do Sandboxie, projeto de código aberto de David Xanatos (xanasoft.com) e Sandboxie Holdings, LLC, distribuído sem modificações sob a licença GNU GPL v3.",
                Left = 16, Top = 96, Width = 388, Height = 64
            });
            var l1 = new LinkLabel { Text = "github.com/FabricioRizzz/SandLite", Left = 16, Top = 168, AutoSize = true };
            l1.LinkClicked += delegate { Process.Start("https://github.com/FabricioRizzz/SandLite"); };
            var l2 = new LinkLabel { Text = "github.com/sandboxie-plus/Sandboxie", Left = 16, Top = 192, AutoSize = true };
            l2.LinkClicked += delegate { Process.Start("https://github.com/sandboxie-plus/Sandboxie"); };
            var ok = new Button { Text = "OK", Left = 320, Top = 220, Width = 84, Height = 30, DialogResult = DialogResult.OK };
            f.Controls.AddRange(new Control[] { l1, l2, ok });
            f.AcceptButton = ok;
            f.ShowDialog(this);
        }

        // ---------- motor embutido ----------

        void OfferEngineInstall()
        {
            if (!Engine.PackageAvailable())
            {
                MessageBox.Show(this, "O motor de isolamento não está instalado e a pasta \"motor\" não está ao lado do SandLite.exe.\n\nCopie a pasta completa do SandLite e abra de novo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, "Para isolar programas, o SandLite precisa instalar o motor dele (leva poucos segundos e é feito uma vez só).\n\nO Windows vai pedir permissão de administrador. Instalar agora?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var code = Elevated("--instalar-motor");
            Sbie.Find();
            if (Sbie.StartExe == null || code != 0)
            {
                MessageBox.Show(this, "Não consegui instalar o motor (código " + code + ").\nVeja o arquivo de log em:\n" + Engine.LogFile, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            LoadFavs();
            LoadBoxes();
            Status("Motor instalado. Pronto para usar.");
        }

        void RemoveEngine()
        {
            if (MessageBox.Show(this, "Remover o motor de isolamento deste PC?\n\nOs programas isolados serão fechados. Os arquivos guardados nas caixas continuam em " + Engine.SandboxRoot + ".",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var code = Elevated("--remover-motor");
            if (code != 0) MessageBox.Show(this, "Algo falhou ao remover (código " + code + "). Log:\n" + Engine.LogFile, Text);
            else MessageBox.Show(this, "Motor removido. Se o Windows pedir, reinicie o PC para terminar.", Text);
            reallyClose = true;
            Close();
        }

        int Elevated(string arg)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, arg) { UseShellExecute = true, Verb = "runas" });
                p.WaitForExit();
                return p.ExitCode;
            }
            catch { return -1; }   // usuário recusou a permissão
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--instalar-motor") { Environment.Exit(Engine.Install()); return; }
            if (args.Length > 0 && args[0] == "--remover-motor") { Environment.Exit(Engine.Remove()); return; }
            if (args.Length > 0 && args[0] == "--pos-instalar") { Environment.Exit(Setup.PostInstall(!args.Contains("--sem-atalho"))); return; }
            if (args.Length > 0 && args[0] == "--desinstalar") { Application.EnableVisualStyles(); Environment.Exit(Setup.Uninstall()); return; }
            bool first;
            using (new System.Threading.Mutex(true, "SandLite_unico", out first))
            {
                if (!first) { MessageBox.Show("O SandLite já está aberto (veja o ícone perto do relógio).", "SandLite"); return; }
                Application.EnableVisualStyles();
                Application.Run(new MainForm());
            }
        }
    }
}

