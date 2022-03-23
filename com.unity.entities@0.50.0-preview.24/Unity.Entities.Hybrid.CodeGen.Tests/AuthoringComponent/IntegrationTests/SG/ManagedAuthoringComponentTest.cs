#if !UNITY_DISABLE_MANAGED_COMPONENTS

using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{
#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif
    public class ManagedAuthoringComponent : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void ManagedAuthoringComponentTest()
        {
            const string typeName = "ManagedAuthoringComponent";
            string code =
                $@"using Unity.Entities;

                   [GenerateAuthoringComponent]
                   public class {typeName} : IComponentData
                   {{
                       public float FloatValue;
                       public int IntValue;
                       public string StringValue;
                       public Entity Prefab;
                       public Entity[] ListOfPrefabs;
                   }}";

            RunAuthoringComponentSourceGenerationTest(code, new GeneratedType{Name = $"{typeName}Authoring"});
        }
    }
}

#endif
