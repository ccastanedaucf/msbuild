using System;
using System.Collections.Generic;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain
{
    internal class ResolveAssemblyReferenceEvaluation
    {
        internal ResolveAssemblyReferenceRequest Input { get; set; }

        internal ResolveAssemblyReferenceResponse Output { get; set; }

        internal HashSet<string> TouchedDirectories { get; set; }

        internal HashSet<string> TouchedFiles { get; set; }

        internal DateTime LastVerifiedCleanTime { get; set; }

        internal void MarkClean()
        {
            LastVerifiedCleanTime = DateTime.Now;
        }

        // tie filename -> evaluations
        // evaluation -> subscribe to changes
        // need to check if valid : o(n) time
        // any change will always be newer
        // watcher sends to a queue
        // funnel into single proc
        // could do concurrent dictionary
        // fileChangeQueue
        // O(num changes)
        // poll changes, check if any newer
        // what to do when a new change comes in - need to update other evaluations





        // each path -> list of evaluations
        // change comes through
        //   add to queue
        // execute rar
        //   poll changes
        //   invalidate lists
        // 

        // or each path -> last modification time
        // change comes through
        //   add to queue
        //  execute rar
        //    poll changes
        //    update times
        //    still have to compare all
        //    observer? if time for a path changes, mark clean with new date
    }
}
