using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.Cache
{
    internal interface IEvaluationWatcher
    {
        bool IsClean(ResolveAssemblyReferenceEvaluation evaluation);

        void Watch(ResolveAssemblyReferenceEvaluation evaluation);
    }
}
