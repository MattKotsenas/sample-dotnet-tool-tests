using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Echo
{
    public class EchoTask : Task
    {
        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.High, "Hello, world!");
            return true;
        }
    }
}