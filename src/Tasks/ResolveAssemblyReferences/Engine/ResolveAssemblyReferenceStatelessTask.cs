using Microsoft.Build.Tasks.ResolveAssemblyReferences.Abstractions;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Engine
{
    internal class ResolveAssemblyReferenceStatelessTask : IResolveAssemblyReferenceTask
    {
        public ResolveAssemblyReferenceTaskOutput Execute(ResolveAssemblyReferenceTaskInput input)
        {
            var rar = new ResolveAssemblyReference { Input = input };
            rar.Execute();
            return rar.Output;
        }
    }
}
