using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks
{
    [Serializable]
    public class ResolveAssemblyReferenceResponse
    {
        public ITaskItem[] CopyLocalFiles { get; set; }

        public string DependsOnNETStandard { get; set; }

        public string DependsOnSystemRuntime { get; set; }

        public ITaskItem[] FilesWritten { get; set; }

        public ITaskItem[] RelatedFiles { get; set; }

        public ITaskItem[] ResolvedDependencyFiles { get; set; }

        public ITaskItem[] ResolvedFiles { get; set; }

        public ITaskItem[] SatelliteFiles { get; set; }

        public ITaskItem[] ScatterFiles { get; set; }

        public ITaskItem[] SerializationAssemblyFiles { get; set; }

        public ITaskItem[] SuggestedRedirects { get; set; }

        public IList<LazyFormattedBuildEventArgs> BuildEvents { get; set; }
    }
}
