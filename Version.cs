// Versione di CorbolPrompt. Cambiala qui prima di una nuova distribuzione.
using System.Reflection;

[assembly: AssemblyProduct("CorbolPrompt")]
[assembly: AssemblyCopyright(CorbolPrompt.AppVersion.Copyright)]
[assembly: AssemblyVersion("0.4.2.0")]
[assembly: AssemblyFileVersion("0.4.2.0")]

namespace CorbolPrompt
{
    static class AppVersion
    {
        public const string Author = "Mauro Corboli";
        public const string Copyright = "Copyright © 2026 " + Author + " - MIT License";
        public const string RepoUrl = "https://github.com/corbolma/CorbolPrompt";
        public const string Text = "0.4.2";
    }
}
