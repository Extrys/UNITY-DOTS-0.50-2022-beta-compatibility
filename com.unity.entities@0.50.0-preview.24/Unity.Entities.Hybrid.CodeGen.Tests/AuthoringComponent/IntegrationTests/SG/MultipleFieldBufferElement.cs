using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{
#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif
    public class MultipleFieldBufferElementAuthoringComponent : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void MultipleFieldBufferElementAuthoringComponentTest()
        {
            const string typeName = "MultipleFieldBufferElementAuthoringComponent";
            string code =
                $@"using Unity.Entities;

               [GenerateAuthoringComponent]
               public struct {typeName} : IBufferElementData
               {{
                    public bool MyBool;
                    public float MyFloat;
                    public int MyInt;
               }}";

            RunAuthoringComponentSourceGenerationTest(
                code,
                new GeneratedType
                {
                    Name = $"{typeName}Authoring"
                },
                new GeneratedType
                {
                    Name = $"___{typeName}GeneratedClass___",
                    IsNestedType = true,
                    ParentTypeName = $"{typeName}Authoring"
                } );
        }
    }
}
