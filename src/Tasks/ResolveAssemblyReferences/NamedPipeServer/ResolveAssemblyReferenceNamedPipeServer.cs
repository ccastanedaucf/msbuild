using System.IO.Pipes;

using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Engine;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Serialization;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.Cache;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.TaskGateway;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.NamedPipeServer
{
    public class ResolveAssemblyReferenceNamedPipeServer
    {
        private const string PipeName = "ResolveAssemblyReference.Pipe";

        private NamedPipeServerStream Pipe { get; } = new NamedPipeServerStream
        (
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.WriteThrough,
            16384,
            16384
        );

        private ResolveAssemblyReferenceCacheService RarService { get; }

        public ResolveAssemblyReferenceNamedPipeServer()
        {
            System.Threading.Tasks.Task.Run(() => { BondDeserializer<ResolveAssemblyReferenceRequest>.Initialize(); });
            System.Threading.Tasks.Task.Run(() => { BondSerializer<ResolveAssemblyReferenceResponse>.Initialize(); });

            var rarTask = new ResolveAssemblyReferenceStatelessTask();
            var taskGatewayService = new ResolveAssemblyReferenceTaskGatewayService(rarTask);

            var evaluationWatcher = new EvaluationWatcher();
            var cacheService = new ResolveAssemblyReferenceCacheService(taskGatewayService, evaluationWatcher);

            RarService = cacheService;
        }

        public void Start()
        {
            while (true)
            {
                Pipe.WaitForConnection();
                HandleRequest();
                Pipe.WaitForPipeDrain();
                Pipe.Disconnect();
            }
        }

        private void HandleRequest()
        {
            ResolveAssemblyReferenceRequest req = BondDeserializer<ResolveAssemblyReferenceRequest>.Deserialize(Pipe);
            var resp = RarService.ResolveAssemblyReferences(req);
            BondSerializer<ResolveAssemblyReferenceResponse>.Serialize(Pipe, resp);
        }
    }
}
