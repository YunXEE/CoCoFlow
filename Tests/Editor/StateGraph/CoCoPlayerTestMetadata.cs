using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    // 测试通过反射检查公开 API 和内部资源计数器，需保留被检查的元数据。
    // 此描述仅在包含测试的 Player 构建中生效，不改变普通游戏的裁剪。
    public sealed class CoCoPlayerTestMetadata : IUnityLinkerProcessor
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
  <assembly fullname=""CoCoFlow.Runtime.Core.StateFlow"">
    <type fullname=""CoCoFlow.Runtime.Core.CoCoContextFrameArena"" preserve=""nothing"">
      <property name=""Capacity"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.Core.CoCoContextFrameStorage"" preserve=""nothing"">
      <property name=""Buffer"" />
      <property name=""RetainCount"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.Core.CoCoTemporalHistory"" preserve=""nothing"">
      <property name=""MaxEncodedSize"" />
      <property name=""AllocatedPayloadBytes"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.Core.CoCoTemporalHistory/Entry"" preserve=""nothing"">
      <property name=""Payload"" />
    </type>
    <type fullname=""CoCoFlow.Runtime.Core.CoCoContextFrameLayout"" preserve=""nothing"">
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
