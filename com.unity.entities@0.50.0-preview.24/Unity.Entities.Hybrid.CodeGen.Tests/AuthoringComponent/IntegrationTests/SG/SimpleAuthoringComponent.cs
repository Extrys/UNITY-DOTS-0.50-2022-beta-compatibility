using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{
#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif
    public class SimpleAuthoringComponent : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void SimpleAuthoringComponentTest()
        {
            const string typeName = "SimpleAuthoringComponent";
            string codeString =
                $@"using Unity.Entities;

               [GenerateAuthoringComponent]
               public struct {typeName} : IComponentData
               {{
                    public float FloatValue;
                    public int IntValue;
               }}";

            RunAuthoringComponentSourceGenerationTest(cSharpCode: codeString, new GeneratedType{Name = $"{typeName}Authoring"});
        }
    }
}
