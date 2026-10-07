using System;
using System.IO;
using System.Reflection;
namespace ElevenLabsMusicGenerator
{
    internal static class PlaybackDependency
    {
        private static bool initialized;
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var expected = new AssemblyName("NAudio, Version=1.10.0.0, Culture=neutral, PublicKeyToken=null");
            var requested = new AssemblyName(args.Name);
            if (requested.Name != expected.Name || requested.Version != expected.Version || requested.GetPublicKeyToken().Length != 0) return null;
            using (var stream = typeof(PlaybackDependency).Assembly.GetManifestResourceStream("ElevenLabsMusicGenerator.NAudio.dll"))
            {
                if (stream == null) return null;
                using (var bytes = new MemoryStream()) { stream.CopyTo(bytes); return Assembly.Load(bytes.ToArray()); }
            }
        }
    }
}
