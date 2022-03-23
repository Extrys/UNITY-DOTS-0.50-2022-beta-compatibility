using System;
using System.Collections;
using NUnit.Framework;
using Unity.Editor.Bridge;
using Unity.Properties.UI;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;
using Object = UnityEngine.Object;

namespace Unity.Entities.Editor.Tests
{
    abstract class EntityWindowIntegrationTestFixture : BaseTestFixture
    {
        const string k_WorldName = "Test World";

        protected TestHierarchyHelper AssertHelper { get; private set; }
        protected EntityManager EntityManager { get; private set; }

        protected EntityHierarchyWindow Window { get; private set; }
        protected IEntityHierarchyState State => Window.State;
        protected VisualElement WindowRoot => Window.rootVisualElement;
        protected SearchElement SearchElement => WindowRoot.Q<SearchElement>();
        protected EntityHierarchyTreeView FullView => WindowRoot.Q<EntityHierarchyTreeView>(Constants.EntityHierarchy.FullViewName);
        protected ListView SearchView => WindowRoot.Q<ListView>(Constants.EntityHierarchy.SearchViewName);

        World m_PreviousWorld;
        PlayerLoopSystem m_PreviousPlayerLoop;

        protected IEnumerator UpdateLiveLink()
        {
            LiveConversionConnection.GlobalDirtyLiveConversion();
            yield return SkipAnEditorFrameAndDiffingUtility();
        }

        protected IEnumerator SkipAnEditorFrameAndDiffingUtility()
        {
            yield return SkipAnEditorFrame();
            Window.Update();
        }

        protected static IEnumerator SkipAnEditorFrame()
        {
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

            // Yield twice to ensure EditorApplication.update was invoked before resuming.
            yield return null;
            yield return null;
        }

        protected IEnumerator ReloadWindow()
        {
            DestroyWindow(Window, preserveViewData: true);
            AssertHelper = null;

            yield return SkipAnEditorFrame();

            Window = CreateWindow();
            AssertHelper = new TestHierarchyHelper(Window.State);

            yield return SkipAnEditorFrameAndDiffingUtility();
            yield return SkipAnEditorFrame();
        }

        public override void OneTimeSetUp()
        {
            var existingInstances = EditorWindowBridge.GetEditorWindowInstances<EntityHierarchyWindow>();
            foreach (var entityHierarchyWindow in existingInstances)
            {
                DestroyWindow(entityHierarchyWindow);
            }

            base.OneTimeSetUp();
        }

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            DefaultWorldInitialization.Initialize(k_WorldName, true);
            EntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            Window = CreateWindow();
            AssertHelper = new TestHierarchyHelper(Window.State);

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<SceneSystem>().LoadSceneAsync(SubScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            World.DefaultGameObjectInjectionWorld.Update();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyWindow(Window);
            AssertHelper = null;

            World.DefaultGameObjectInjectionWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            m_PreviousWorld = null;
            EntityManager = default;

            PlayerLoop.SetPlayerLoop(m_PreviousPlayerLoop);

            TearDownSubScene();
        }

        protected void TearDownSubScene()
        {
            foreach (var rootGO in SubScene.EditingScene.GetRootGameObjects())
                Object.DestroyImmediate(rootGO);
        }

        static EntityHierarchyWindow CreateWindow()
        {
            var window = EditorWindow.CreateWindow<EntityHierarchyWindow>();
            window.DisableDifferCooldownPeriod = true;
            window.Update();
            window.ChangeCurrentWorld(World.DefaultGameObjectInjectionWorld);

            return window;
        }

        static void DestroyWindow(EditorWindow window, bool preserveViewData = false)
        {
            window.Close();
            Object.DestroyImmediate(window);
            Assert.That(EntityHierarchyItemView.ItemsScheduledForPeriodicCheck, Is.Empty);

            if (!preserveViewData)
                EditorWindowBridge.ClearPersistentViewData(window);
        }
    }
}
