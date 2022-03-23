using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{

#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif


    public class AuthoringComponentWithReferencedPrefab : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void AuthoringComponentWithReferencedPrefabTest()
        {
            const string typeName = "Component";
            string codeString =
                $@"using Unity.Entities;

               [GenerateAuthoringComponent]
               public struct {typeName} : IComponentData
               {{
                   public Entity PrefabA;
                   public Entity PrefabB;
                   public float FloatValue;
                   public int IntValue;
                   public Entity PrefabC;
               }}";

            RunAuthoringComponentSourceGenerationTest(codeString, new GeneratedType{Name = $"{typeName}Authoring"});
        }
    }
}
