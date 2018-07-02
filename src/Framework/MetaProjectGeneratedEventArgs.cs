using System.Text;
using System.Xml;

namespace Microsoft.Build.Framework
{
    public class MetaProjectGeneratedEventArgs : BuildMessageEventArgs
    {
        public string metaProjectPath;

        public MetaProjectGeneratedEventArgs(string metaProjectPath)
        {
            this.metaProjectPath = metaProjectPath;
        }
    }
}
