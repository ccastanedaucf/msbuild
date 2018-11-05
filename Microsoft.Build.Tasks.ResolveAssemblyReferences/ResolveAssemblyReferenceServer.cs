using Microsoft.Build.Tasks.ResolveAssemblyReferences.NamedPipeServer;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences
{
    public class ResolveAssemblyReferenceServer
    {
        public static void Main(string[] args)
        {
            var server = new ResolveAssemblyReferenceNamedPipeServer();
            server.Start();
        }
    }
}
