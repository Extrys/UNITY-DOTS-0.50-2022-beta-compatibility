using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.Hybrid.Tests;
using UnityEngine.LowLevel;

namespace Unity.Entities.Tests
{
    public class DefaultWorldInitializationTests
    {
        World m_World;
        TestWithCustomDefaultGameObjectInjectionWorld m_CustomInjectionWorld;
        private PlayerLoopSystem m_PrevPlayerLoop;

        [OneTimeSetUp]
        public void Setup()
        {
            m_PrevPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            m_CustomInjectionWorld.Setup();
            DefaultWorldInitialization.Initialize("TestWorld", false);
            m_World = World.DefaultGameObjectInjectionWorld;
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            m_CustomInjectionWorld.TearDown();
            PlayerLoop.SetPlayerLoop(m_PrevPlayerLoop);
        }

        [Test]
        public void Systems_CalledViaGetOrCreateSystem_AreCreated()
        {
            m_World.GetOrCreateSystem<SystemWithGetOrCreate>();
            Assert.IsNotNull(m_World.GetExistingSystem<GetOrCreateTargetSystem>(), $"{nameof(GetOrCreateTargetSystem)} was not automatically created");
        }

        [Test]
        public void Systems_WithCyclicReferences_AreAllCreated()
        {
            m_World.GetOrCreateSystem<CyclicReferenceSystemA>();
            Assert.IsNotNull(m_World.GetExistingSystem<CyclicReferenceSystemA>(), nameof(CyclicReferenceSystemA) + " was not created");
            Assert.IsNotNull(m_World.GetExistingSystem<CyclicReferenceSystemB>(), nameof(CyclicReferenceSystemB) + " was not created");
            Assert.IsNotNull(m_World.GetExistingSystem<CyclicReferenceSystemC>(), nameof(CyclicReferenceSystemC) + " was not created");
        }

        partial class SystemWithGetOrCreate : SystemBase
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                World.GetOrCreateSystem<GetOrCreateTargetSystem>();
            }

            protected override void OnUpdate()
            {
            }
        }

        partial class GetOrCreateTargetSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }
        }

        partial class CyclicReferenceSystemA : SystemBase
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                World.GetOrCreateSystem<CyclicReferenceSystemB>();
            }

            protected override void OnUpdate() {}
        }

        partial class CyclicReferenceSystemB : SystemBase
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                World.GetOrCreateSystem<CyclicReferenceSystemC>();
            }

            protected override void OnUpdate() {}
        }

        partial class CyclicReferenceSystemC : SystemBase
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                World.GetOrCreateSystem<CyclicReferenceSystemA>();
            }

            protected override void OnUpdate() {}
        }
    }
}
