using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{
#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif
    public class SingleFieldBufferElementAuthoringComponent : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void SingleFieldBufferElementAuthoringComponentTest()
        {
            const string typeName = "SingleFieldBufferElementAuthoringComponent";
            string code =
                $@"using Unity.Entities;

               [GenerateAuthoringComponent]
               public struct {typeName} : IBufferElementData
               {{
                    public int Value;
               }}";

            RunAuthoringComponentSourceGenerationTest(code, new GeneratedType{Name = $"{typeName}Authoring"});
        }
    }
}
