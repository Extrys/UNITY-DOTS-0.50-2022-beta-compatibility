#if !UNITY_DISABLE_MANAGED_COMPONENTS
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class EntitiesForEachWithStructuralChanges : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using Unity.Entities;
using Unity.Entities.CodeGen.Tests;
using Unity.Entities.CodeGen.Tests.TestTypes;

partial class EntitiesForEachWithStructuralChanges : SystemBase
{{
    protected override void OnUpdate()
    {{
        float delta = 5.0f;
        Entities
            .WithoutBurst()
            .WithStructuralChanges()
            .ForEach((Entity id,
                SpeedInAnotherAssembly managedData,
                SharedDataInAnotherAssembly sharedData,
                ref TranslationInAnotherAssembly refData,
                ref DynamicBuffer<TestBufferElementInAnotherAssembly> dynamicBuffer,
                in VelocityInAnotherAssembly inData,
                in TagComponentInAnotherAssembly inTag) =>
                {{
                    refData.Value += inData.Value + sharedData.Value + delta;
                    if (refData.Value > 10.0f)
                        EntityManager.RemoveComponent<Translation>(id);
                    dynamicBuffer.Add(managedData.Value);
                }}).Run();
    }}
}}";
        [Test]
        public void EntitiesForEachWithStructuralChangesTest()
        {
            RunTest(_testSource, new GeneratedType {Name = "EntitiesForEachWithStructuralChanges"});
        }
    }
}
#endif
