using System;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public enum BuildEventArgsType
    {
        Error,
        Message,
        Warning,
    }

    public class ResolveAssemblyReferenceBuildEventArgs
    {
        public BuildEventArgsType BuildEventArgsType { get; set; } = BuildEventArgsType.Error;

        public string Message { get; set; } = string.Empty;

        public string? HelpKeyword { get; set; } = string.Empty;

        public string? SenderName { get; set; } = string.Empty;

        public long EventTimestamp { get; set; }

        public string[] MessageArgs { get; set; } = [];

        public int Importance { get; set; }

        public string? Subcategory { get; set; } = string.Empty;

        public string? Code { get; set; } = string.Empty;

        public string? File { get; set; } = string.Empty;

        public int LineNumber { get; set; }

        public int ColumnNumber { get; set; }

        public int EndLineNumber { get; set; }

        public int EndColumnNumber { get; set; }
    }
}
