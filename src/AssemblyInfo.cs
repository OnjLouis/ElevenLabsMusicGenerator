using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("ElevenLabs Music Generator")]
[assembly: AssemblyDescription("Accessible portable Windows utility for ElevenLabs music and sound effects generation")]
[assembly: AssemblyCompany("Andre Louis")]
[assembly: AssemblyProduct("ElevenLabs Music Generator")]
[assembly: AssemblyCopyright("Copyright 2026 Andre Louis")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion(ElevenLabsMusicGenerator.AppVersion.Full)]
[assembly: AssemblyFileVersion(ElevenLabsMusicGenerator.AppVersion.Full)]

namespace ElevenLabsMusicGenerator
{
    internal static class AppVersion
    {
        public const string Short = "1.2.0";
        public const string Full = Short + ".0";
    }
}
