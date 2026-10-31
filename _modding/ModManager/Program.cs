using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ChimeraModManager
{
    internal static class Program
    {
        internal const string GameExe = "StarCrawlers Chimera.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            // Newtonsoft.Json is embedded in the exe; hand it over before anything that uses it is touched.
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                if (new AssemblyName(e.Name).Name != "Newtonsoft.Json") return null;
                using (Stream st = typeof(Program).Assembly.GetManifestResourceStream("Newtonsoft.Json.dll"))
                {
                    if (st == null) return null;
                    var bytes = new byte[st.Length];
                    st.Read(bytes, 0, bytes.Length);
                    return Assembly.Load(bytes);
                }
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            return Run(args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            string game = Arg(args, "--game") ?? FindGame(AppDomain.CurrentDomain.BaseDirectory) ?? FindGame(Environment.CurrentDirectory) ?? AskForGame();
            if (game == null) return 1;
            var mods = new ModFiles(game);
#if !PLAYER
            string sealMod = Arg(args, "--seal-audio");
            if (sealMod != null) return SealAudio.Run(sealMod, Array.IndexOf(args, "--remove-loose") >= 0, Console.WriteLine);
            string selfTest = Arg(args, "--selftest");
            if (selfTest != null) return SelfTest.Run(mods, selfTest);
#endif
            Application.Run(new MainForm(mods));
            return 0;
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>The folder with the game's exe: this one or one above it.</summary>
        private static string FindGame(string start)
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, GameExe))) return d.FullName;
            return null;
        }

        private static string AskForGame()
        {
            MessageBox.Show("Breachwork Mod Manager belongs in the StarCrawlers Chimera game folder (next to \"" + GameExe + "\").\n\n" +
                "Pick the game folder now. In Steam: right-click StarCrawlers Chimera > Manage > Browse local files.",
                "Breachwork Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using (var dlg = new FolderBrowserDialog { Description = "The StarCrawlers Chimera game folder" })
                while (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(Path.Combine(dlg.SelectedPath, GameExe))) return dlg.SelectedPath;
                    MessageBox.Show("That folder has no \"" + GameExe + "\".", "Breachwork Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            return null;
        }
    }
}
