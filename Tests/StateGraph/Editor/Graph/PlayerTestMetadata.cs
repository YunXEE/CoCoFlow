using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class PlayerTestMetadata : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data)
        {
            if ((report.summary.options & BuildOptions.IncludeTestAssemblies) == 0)
            {
                return null;
            }

            string directory = Path.GetFullPath("Library/CoCoFlow/Tests");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "link.xml");
            File.WriteAllText(path, @"<linker>
  <assembly fullname=""UnityEngine.AnimationModule"">
    <type fullname=""UnityEngine.Animations.AnimatorControllerPlayable"" preserve=""all"" />
  </assembly>
  <assembly fullname=""Unity.ResourceManager"">
    <type fullname=""UnityEngine.ResourceManagement.ResourceManager/CompletedOperation`1"" preserve=""nothing"">
      <method signature=""System.Void .ctor()"" />
    </type>
  </assembly>
  <assembly fullname=""CoCoFlow.Runtime.StateGraph.Contracts"">
    <type fullname=""CoCoFlow.Runtime.StateGraph.ContextFrameArena"" preserve=""nothing"">
      <property name=""Capacity"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.StateGraph.ContextFrameStorage"" preserve=""nothing"">
      <property name=""Buffer"" />
      <property name=""RetainCount"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.StateGraph.TemporalHistory"" preserve=""nothing"">
      <property name=""MaxEncodedSize"" />
      <property name=""AllocatedPayloadBytes"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.StateGraph.TemporalHistory/Entry"" preserve=""nothing"">
      <property name=""Payload"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.StateGraph.ContextFrameLayout"" preserve=""nothing"">
      <property name=""ByteSize"" />
    </type>
  </assembly>
  <assembly fullname=""CoCoFlow.Runtime.Modules.Map.Temporal"">
    <type fullname=""CoCoFlow.Runtime.Modules.Map.Temporal.RegionTemporalRuntime/RegionTemporalFrame"" preserve=""nothing"">
      <property name=""Info"" />
      <property name=""Regions"" />
    </type>
  </assembly>
</linker>");
            return path;
        }
    }
}
