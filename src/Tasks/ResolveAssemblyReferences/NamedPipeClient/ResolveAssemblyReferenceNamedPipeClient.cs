using System.IO.Pipes;

using Microsoft.Build.Tasks.ResolveAssemblyReferences.Abstractions;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Engine;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Serialization;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.NamedPipeClient
{
    internal class ResolveAssemblyReferenceNamedPipeClient : IResolveAssemblyReferenceService
    {
        private const string PipeName = "ResolveAssemblyReference.Pipe";

        private ResolveAssemblyReferenceServiceGatewayTask RarTask { get; }

        internal ResolveAssemblyReferenceNamedPipeClient()
        {
            RarTask = new ResolveAssemblyReferenceServiceGatewayTask(this);
        }

        internal ResolveAssemblyReferenceTaskOutput Execute(ResolveAssemblyReferenceTaskInput taskInput)
        {
            return RarTask.Execute(taskInput);
        }

        public ResolveAssemblyReferenceResponse ResolveAssemblyReferences(ResolveAssemblyReferenceRequest req)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.WriteThrough))
            {
                pipe.Connect();
                BondSerializer<ResolveAssemblyReferenceRequest>.Serialize(pipe, req);
                return BondDeserializer<ResolveAssemblyReferenceResponse>.Deserialize(pipe);
            }
        }

        /*
        private void LogBuildEvents(IList<LazyFormattedBuildEventArgs> buildEvents)
        {
            return;

            foreach (LazyFormattedBuildEventArgs buildEvent in buildEvents)
            {
                switch (buildEvent)
                {
                    case CustomBuildEventArgs customEventArgs:
                        BuildEngine.LogCustomEvent(customEventArgs);
                        break;
                    case BuildErrorEventArgs errorEventArgs:
                        BuildEngine.LogErrorEvent(errorEventArgs);
                        break;
                    case BuildMessageEventArgs messageEventArgs:
                        BuildEngine.LogMessageEvent(messageEventArgs);
                        break;
                    case BuildWarningEventArgs warningEventArgs:
                        BuildEngine.LogWarningEvent(warningEventArgs);
                        break;
                }
            }
        }
        */
    }
}
