using System.Collections.Generic;

using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain
{
    public partial class ResolveAssemblyReferenceResponse
    {
        public HashSet<string> TrackedFiles { get; set; }

        public HashSet<string> TrackedDirectories { get; set; }

        public IList<LazyFormattedBuildEventArgs> BuildEvents { get; set; }
    }
}
