using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
    class GenericComponentDataTest  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;

            public partial class GenericComponentDataSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    EntityManager.CreateEntity(typeof(GenericDataType<int>));
                    SetSingleton(new GenericDataType<int>() { value = 10 });
                }

                public struct GenericDataType<T> : IComponentData where T : unmanaged
                {
                    public T value;
                }
            }";

        [Test]
        public void GenericSingletonDoesNotTriggerEntityQueryGeneration_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "GenericComponentDataSystem"
                });
        }
    }
}
