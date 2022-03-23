using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
    class AccessingGenericSingletonsSystemTest  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using System;
            using Unity.Entities;
            using Unity.Entities.Tests;
            using UnityEngine;

            public partial class AccessingGenericSingletonsSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    EntityManager.CreateEntity(typeof(EcsTestData));
                    GenericMethodWithSingletonAccess(new EcsTestData(10));
                    if (GetSingleton<EcsTestData>().value == 10)
                    {
                        Debug.Log(""Yay!"");
                    }
                }

                // The SetSingleton() and GetSingleton() calls in this method should not trigger generation of Entity Query fields
                T GenericMethodWithSingletonAccess<T>(T value) where T : struct, IComponentData
                {
                    SetSingleton(value);
                    return GetSingleton<T>();
                }
            }";

        [Test]
        public void AccessingGenericSingletons_DoNotGenerateEntityQueryFields_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "AccessingGenericSingletonsSystem"
                });
        }
    }
}
