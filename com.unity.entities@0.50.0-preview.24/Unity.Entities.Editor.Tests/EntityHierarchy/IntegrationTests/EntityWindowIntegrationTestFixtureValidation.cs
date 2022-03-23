using System.Collections;
using NUnit.Framework;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unity.Entities.Editor.Tests
{
    // Validates expectations for the result of OneTimeSetUp + SetUp in EntityWindowIntegrationTestFixture
    class EntityWindowIntegrationTestFixtureValidation : EntityWindowIntegrationTestFixture
    {
        [UnityTest]
        public IEnumerator TestSetup_ProducesExpectedResult()
        {
            // Window was initialized properly
            Assert.That(EditorWindow.HasOpenInstances<EntityHierarchyWindow>(), Is.True);
            Assert.That(Window.World, Is.EqualTo(World.DefaultGameObjectInjectionWorld));
            Assert.That(Window.DisableDifferCooldownPeriod, Is.True);

            // Initial setup is clean and all is as expected
            Assert.That(SubScene.name, Is.EqualTo(SubSceneName));
            Assert.That(SubScene.SceneName, Is.EqualTo(SubSceneName));
            Assert.That(SubScene.CanBeLoaded(), Is.True);
            Assert.That(SubScene.IsLoaded, Is.True);
            Assert.That(SubScene.EditingScene.isLoaded, Is.True);
            Assert.That(SubScene.EditingScene.isSubScene, Is.True);

            Assert.That(SubSceneRoot.name, Is.EqualTo(SubSceneName));
            Assert.That(SubSceneRoot, Is.EqualTo(SubScene.gameObject));

            Assert.That(SubSceneRoot.GetComponent<SubScene>(), Is.Not.Null);
            Assert.That(SubSceneRoot.transform.childCount, Is.EqualTo(0));

            Assert.That(Scene.rootCount, Is.EqualTo(1));
            Assert.That(SubScene.EditingScene.rootCount, Is.EqualTo(0));

            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);

            Assert.That(SubScene.EditingScene.rootCount, Is.EqualTo(1));
            Assert.That(SubScene.EditingScene.GetRootGameObjects()[0], Is.EqualTo(go));
            Assert.That(go.scene, Is.EqualTo(SubScene.EditingScene));

            // Parenting into a SubScene
            var childGO = new GameObject("childGO");
            Assert.That(childGO.scene, Is.EqualTo(Scene));

            childGO.transform.parent = go.transform;
            Assert.That(childGO.scene, Is.EqualTo(SubScene.EditingScene));

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection
            Assert.That(EntityManager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(3));

            yield return UpdateLiveLink();

            Assert.That(Window.World, Is.EqualTo(World.DefaultGameObjectInjectionWorld));

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection - 4. Converted `go` - 5. Converted `childGO`
            Assert.That(EntityManager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(5));

            // TearDown properly cleans-up the SubScene
            TearDownSubScene();

            Assert.That(SubScene.EditingScene.rootCount, Is.EqualTo(0));

            yield return UpdateLiveLink();

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection
            Assert.That(EntityManager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(3));
        }
    }
}
