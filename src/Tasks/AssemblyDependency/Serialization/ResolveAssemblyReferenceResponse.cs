using System;
using System.Collections.Generic;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public class ResolveAssemblyReferenceResponse
    {
        public List<ResolveAssemblyReferenceBuildEventArgs> BuildEventArgsQueue { get; set; } = [];

        public int NumCopyLocalFiles { get; set; }

        public string DependsOnNETStandard { get; set; } = string.Empty;

        public string DependsOnSystemRuntime { get; set; } = string.Empty;

        public ReadOnlyTaskItem[] FilesWritten { get; set; } = [];

        public ReadOnlyTaskItem[] RelatedFiles { get; set; } = [];

        public ReadOnlyTaskItem[] ResolvedDependencyFiles { get; set; } = [];

        public ReadOnlyTaskItem[] ResolvedFiles { get; set; } = [];

        public ReadOnlyTaskItem[] SatelliteFiles { get; set; } = [];

        public ReadOnlyTaskItem[] ScatterFiles { get; set; } = [];

        public ReadOnlyTaskItem[] SerializationAssemblyFiles { get; set; } = [];

        public ReadOnlyTaskItem[] SuggestedRedirects { get; set; } = [];

        public ReadOnlyTaskItem[] UnresolvedAssemblyConflicts { get; set; } = [];
    }
}
