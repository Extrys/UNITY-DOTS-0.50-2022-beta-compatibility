using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class EntitiesForEachInNestedNamespacesAndClasses : LambdaJobsSourceGenerationIntegrationTest
    {
        const string _testSource = @"
using Unity.Mathematics;

namespace EntitiesForEachInNested
{
    using Unity.Entities;
    namespace Namespaces
    {
        using Unity.Entities.CodeGen.Tests;

        partial class AndClasses
        {
            partial class SomeSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation translation) => { translation.Value += 1.0f; })
                        .Schedule();
                }
            }

            partial class SomeOtherSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation translation) => { translation.Value += 1.0f; })
                        .Schedule();
                }
            }
        }
    }
}
";

        [Test]
        public void EntitiesForEachInNestedNamespacesAndClassesTest()
        {
            RunTest(_testSource, new GeneratedType {Name = "EntitiesForEachInNested.Namespaces.AndClasses"});
        }
    }
}
