#if !DOTS_DISABLE_DEBUG_NAMES
using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Scenes;
using Unity.Scenes.Editor;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;
using Object = UnityEngine.Object;

namespace Unity.Entities.Editor.Tests
{
    [TestFixture]
    [SuppressMessage("ReSharper", "Unity.InefficientPropertyAccess")]
    class EntityWindowIntegrationTests : EntityWindowIntegrationTestFixture
    {
        protected override string SceneName { get; } = nameof(EntityWindowIntegrationTests);

        [UnityTest]
        public IEnumerator BasicSubsceneBasedParenting_ProducesExpectedResult()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);

            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, entityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, entityId, 0)); // "go" Converted Entity

            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Asserts that the names of the scenes were correctly found.
            using (var rootChildren = PooledList<EntityHierarchyNodeId>.Make())
            {
                Window.State.GetChildren(EntityHierarchyNodeId.Root, rootChildren.List);

                var sceneNode = rootChildren.List.Single(child => child.Kind == NodeKind.RootScene);
                Assert.That(Window.State.GetNodeName(sceneNode), Is.EqualTo(SceneName));

                using (var sceneChildren = PooledList<EntityHierarchyNodeId>.Make())
                {
                    Window.State.GetChildren(sceneNode, sceneChildren.List);

                    // Only Expecting a single child here
                    var subsceneNode = sceneChildren.List[0];
                    Assert.That(Window.State.GetNodeName(subsceneNode), Is.EqualTo(SubSceneName));
                }

            }
        }

        [UnityTest]
        public IEnumerator DefaultFoldingState_IsRecreated_AfterReload()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);
            yield return UpdateLiveLink();

            yield return ReloadWindow();

            var visibleItems = FullView.VisibleItems.Select(x => State.GetNodeName(x));
            var expectedSubset = new[]
            {
                SceneName,
                SubSceneName,
                "go"
            };

            // Tests that all of the expected items are found in the "visibleItems" list
            Assert.That(expectedSubset.Except(visibleItems).Any(), Is.False, "Not all expected nodes were found.");
        }

        [UnityTest]
        public IEnumerator FoldingState_IsPreserved_AfterReload()
        {
            // Adding a GameObject hierarchy
            var go = new GameObject("go");
            var go2 = new GameObject("go2");
            var go3 = new GameObject("go3");
            go2.transform.parent = go.transform;
            go3.transform.parent = go2.transform;
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);

            yield return UpdateLiveLink();

            FullView.FastExpandAll();

            yield return ReloadWindow();

            var visibleItems = FullView.VisibleItems.Select(x => State.GetNodeName(x));
            var expectedSubset = new[]
            {
                SceneName,
                SubSceneName,
                "go",
                "go2",
                "go3"
            };

            // Tests that all of the expected items are found in the "visibleItems" list
            Assert.That(expectedSubset.Except(visibleItems).Any(), Is.False, "Not all expected nodes were found.");
        }

        // Scenario:
        // - Add GameObject to root of subscene
        // - Add second GameObject to root of subscene
        // - Parent second GameObject to first
        // - Unparent second GameObject from first
        // - Delete second GameObject
        [UnityTest, Ignore("Randomly fails locally, exception about deallocated EntityManager.")]
        public IEnumerator SubsceneBasedParenting_Scenario1()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);
            yield return UpdateLiveLink();

            // Adding a second GameObject to a SubScene
            var go2 = new GameObject("go2");
            SceneManager.MoveGameObjectToScene(go2, SubScene.EditingScene);
            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),// "go" Converted Entity
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Parent second GameObject to first
            go2.transform.parent = go.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))    // "go" Converted Entity
                        .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Unparent second GameObject from first
            go2.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),// "go" Converted Entity
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Delete second GameObject
            Object.DestroyImmediate(go2);
            yield return UpdateLiveLink();

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go" Converted Entity

            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());
        }

        // Scenario:
        // 3 GameObjects [A, B, C] at the Root of a SubScene
        // Move A into B
        // Move A into C
        // Move B into C
        // Move A back to Root
        // Move B back to Root
        [UnityTest, Ignore("Randomly fails locally, exception about deallocated EntityManager.")]
        public IEnumerator SubsceneBasedParenting_Scenario2()
        {
            var a = new GameObject("A");
            SceneManager.MoveGameObjectToScene(a, SubScene.EditingScene);
            yield return UpdateLiveLink();

            var b = new GameObject("B");
            SceneManager.MoveGameObjectToScene(b, SubScene.EditingScene);
            yield return UpdateLiveLink();

            var c = new GameObject("C");
            SceneManager.MoveGameObjectToScene(c, SubScene.EditingScene);
            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = b.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0));
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = c.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0));
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            b.transform.parent = c.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))
                    .AddChildren(
                         new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),
                         new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0));
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            b.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());
        }

        [UnityTest]
        public IEnumerator Search_ToggleSearchView()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            var other = new GameObject("other");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(other, SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("go");

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));

            SearchElement.Search(string.Empty);

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Search_DisableSearchWhenSearchElementIsHidden()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            var other = new GameObject("other");
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(other, SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("go");

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(SearchView.itemsSource
                            .Cast<EntityHierarchyNodeId>()
                            .Select(x => State.GetNodeName(x)),
                Is.EquivalentTo(new[] { SubSceneName, "go" }));

            var searchIcon = WindowRoot.Q(className: UssClasses.DotsEditorCommon.SearchIcon);
            Window.SendEvent(new Event
            {
                type = EventType.MouseUp,
                mousePosition = searchIcon.worldBound.position
            });

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            Window.SendEvent(new Event
            {
                type = EventType.MouseUp,
                mousePosition = searchIcon.worldBound.position
            });

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(SearchView.itemsSource
                            .Cast<EntityHierarchyNodeId>()
                            .Select(x => State.GetNodeName(x)),
                Is.EquivalentTo(new[] { SubSceneName, "go" }));
        }

        [UnityTest]
        public IEnumerator Search_NameSearch()
        {
            // Adding a bunch of GameObjects to a SubScene
            // We need enough to make sure that:
            // * We get some matches
            // * We get some misses
            // * We cover a search which could confused by multiple tokens

            SceneManager.MoveGameObjectToScene(new GameObject("First Cube"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("First Cube (1)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("First Cube (2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("First Cube (2)(2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("First Cube (3)"), SubScene.EditingScene);

            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube (1)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube (1)(2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube (2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube (3)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Cube (4)"), SubScene.EditingScene);

            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube222"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (1)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (1)(2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (3)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (4)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Cube (4)(2)"), SubScene.EditingScene);

            SceneManager.MoveGameObjectToScene(new GameObject("First Sphere"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Second Sphere"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Sphere"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Sphere (1)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Third Sphere (2)"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("Fourth Sphere"), SubScene.EditingScene);

            SceneManager.MoveGameObjectToScene(new GameObject("The 12 Spherical Cuboids"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("The 12 Spherical Cones"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("th CUB 2");

            yield return null;

            var items = SearchView.itemsSource
                .Cast<EntityHierarchyNodeId>()
                .Select(x => State.GetNodeName(x));

            Assert.That(items, Is.EquivalentTo(new[]
            {
                SubSceneName,
                "Third Cube222", "Third Cube (1)(2)", "Third Cube (2)", "Third Cube (4)(2)", "The 12 Spherical Cuboids"
            }));
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch()
        {
            // Adding a GameObject to a SubScene
            SceneManager.MoveGameObjectToScene(new GameObject("go"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search($"c:{nameof(WorldTime)}");

            yield return null;

            var items = SearchView.itemsSource
                .Cast<EntityHierarchyNodeId>()
                .Select(x => State.GetNodeName(x));

            Assert.That(items, Is.EquivalentTo(new[] { nameof(WorldTime) }));
        }

        [UnityTest]
        public IEnumerator Search_QueryAndNameSearch()
        {
            // Adding a GameObject to a SubScene
            SceneManager.MoveGameObjectToScene(new GameObject("abc"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("def"), SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("ghi"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search($"c:{nameof(EntityGuid)} abc");

            yield return null;

            var items = SearchView.itemsSource
                .Cast<EntityHierarchyNodeId>()
                .Select(x => State.GetNodeName(x));

            Assert.That(items, Is.EquivalentTo(new[] { SubSceneName, "abc" }));
        }

        [UnityTest]
        public IEnumerator Search_NameSearch_NoResult()
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("hello");

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            var centeredMessageElement = WindowRoot.Q<CenteredMessageElement>();
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(centeredMessageElement.Title, Is.EqualTo(EntityHierarchy.NoEntitiesFoundTitle));
            Assert.That(centeredMessageElement.Message, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Search_NameSearch_NoResultThenMatchANewlyCreatedEntity()
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("hello");

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            var centeredMessageElement = WindowRoot.Q<CenteredMessageElement>();
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));

            SceneManager.MoveGameObjectToScene(new GameObject("helloworld"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch_NoResult()
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            SearchElement.Search("c:TypeThatDoesntExist");

            yield return null;

            Assert.That(FullView, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(SearchView, UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            var centeredMessageElement = WindowRoot.Q<CenteredMessageElement>();
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(centeredMessageElement.Title, Is.EqualTo(EntityHierarchy.ComponentTypeNotFoundTitle));
            Assert.That(centeredMessageElement.Message, Is.EqualTo(string.Format(EntityHierarchy.ComponentTypeNotFoundContent, "TypeThatDoesntExist")));
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch_IncludeSpecialEntity([Values(typeof(Prefab), typeof(Disabled))] Type componentType)
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), SubScene.EditingScene);

            yield return UpdateLiveLink();

            var e = EntityManager.CreateEntity();
            EntityManager.SetName(e, "Test entity");
            EntityManager.AddComponent<EntityGuid>(e);
            EntityManager.AddComponent(e, componentType);

            yield return SkipAnEditorFrameAndDiffingUtility();

            var expectedNode = EntityHierarchyNodeId.FromEntity(e);
            Assert.That(FullView.items, Does.Contain(expectedNode));

            SearchElement.Search($"c:{typeof(EntityGuid).FullName}");

            yield return SkipAnEditorFrameAndDiffingUtility();

            Assert.That(SearchView.itemsSource, Does.Contain(expectedNode));
        }

        [UnityTest]
        public IEnumerator FollowDisabledGameObjectState()
        {
            var go = new GameObject("go");
            var disabledGo = new GameObject("disabled go");
            disabledGo.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(disabledGo, SubScene.EditingScene);

            yield return UpdateLiveLink();

            var withDisabledClass = FullView.Query<EntityHierarchyItemView>().ToList().SingleOrDefault(i => i.style.opacity == .5f);
            Assert.That(withDisabledClass, Is.Not.Null);
            Assert.That(withDisabledClass.Q<Label>().text, Is.EqualTo("disabled go"));

            disabledGo.SetActive(true);

            yield return UpdateLiveLink();
            Window.EntityHierarchyItemViewPeriodicCheck();

            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList().Count(i => i.style.opacity == .5f), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShowPrefabInstancesWithCorrectStyle()
        {
            var root = new GameObject("prefab root");
            new GameObject("prefab children").transform.SetParent(root.transform);
            SceneManager.MoveGameObjectToScene(root, SubScene.EditingScene);

            yield return UpdateLiveLink();

            FullView.FastExpandAll();

            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList().Count(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.Prefab)), Is.Zero);
            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList().Count(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.PrefabRoot)), Is.Zero);

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, TestAssetsDirectory + "/MyTestPrefab.prefab", InteractionMode.AutomatedAction, out var success);
            Assert.That(success, Is.True);
            new GameObject("non prefab child").transform.SetParent(root.transform);

            yield return UpdateLiveLink();
            Window.EntityHierarchyItemViewPeriodicCheck();

            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList()
                            .Where(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.Prefab))
                            .Select(i => i.Q<Label>().text),
                        Is.EquivalentTo(new[] { "prefab root", "prefab children" }));
            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList()
                            .Where(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.PrefabRoot))
                            .Select(i => i.Q<Label>().text), Is.EquivalentTo(new[] { "prefab root" }));
            var nonPrefabChild = FullView.Query<EntityHierarchyItemView>().ToList().SingleOrDefault(i => i.Q<Label>().text == "non prefab child");
            Assert.That(nonPrefabChild, Is.Not.Null);
            Assert.That(nonPrefabChild.GetClasses(), Does.Not.Contain(UssClasses.EntityHierarchyWindow.Item.Prefab).And.Not.Contain(UssClasses.EntityHierarchyWindow.Item.PrefabRoot));
        }

        [UnityTest]
        public IEnumerator ShowPrefabAssetsWithCorrectStyle()
        {
            // Fake a prefab asset entity
            var root = EntityManager.CreateEntity(typeof(Prefab), typeof(LinkedEntityGroup));
            EntityManager.SetName(root, "root");
            var children = EntityManager.CreateEntity(typeof(Prefab), typeof(Parent));
            EntityManager.SetName(children, "children");
            EntityManager.SetComponentData(children, new Parent() { Value = root });

            yield return UpdateLiveLink();

            using var query = EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new ComponentType[] { typeof(Prefab) }, Options = EntityQueryOptions.IncludePrefab });
            using var prefabAssetEntities = query.ToEntityArray(Allocator.TempJob);
            Assert.That(prefabAssetEntities.ToArray(), Is.EquivalentTo(new[] { root, children }));

            FullView.FastExpandAll();

            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList()
                            .Where(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.Prefab))
                            .Select(i => i.Q<Label>().text),
                        Is.EquivalentTo(new[] { "root", "children" }));
            Assert.That(FullView.Query<EntityHierarchyItemView>().ToList()
                            .Where(i => i.ClassListContains(UssClasses.EntityHierarchyWindow.Item.PrefabRoot))
                            .Select(i => i.Q<Label>().text),
                        Is.EquivalentTo(new[] { "root" }));
        }

        [UnityTest]
        public IEnumerator Selection_DestroyingSelectedEntityDeselectInView()
        {
            var internalListView = FullView.Q<ListView>();

            var e = EntityManager.CreateEntity();
            var node = EntityHierarchyNodeId.FromEntity(e);

            yield return SkipAnEditorFrameAndDiffingUtility();

            Assert.That(internalListView.currentSelectionIds, Is.Empty);

            EntitySelectionProxy.SelectEntity(EntityManager.World, e);
            yield return null;

            Assert.That(internalListView.currentSelectionIds, Is.EquivalentTo(new[] { node.GetHashCode() }));

            EntityManager.DestroyEntity(e);
            yield return SkipAnEditorFrameAndDiffingUtility();

            Assert.That(internalListView.selectedItems, Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadDynamicSubScene_ShowsNewNodeInTreeView()
        {
            var subSceneName = "DynamicSubScene";
            var targetGO = new GameObject(subSceneName);
            var subsceneArgs = new SubSceneContextMenu.NewSubSceneArgs(targetGO, Scene, SubSceneContextMenu.NewSubSceneMode.EmptyScene);
            var subScene = SubSceneContextMenu.CreateNewSubScene(subSceneName, subsceneArgs, InteractionMode.AutomatedAction);
            SceneManager.MoveGameObjectToScene(new GameObject("go"), subScene.EditingScene);

            Object.DestroyImmediate(targetGO);
            Object.DestroyImmediate(subScene.gameObject);

            yield return UpdateLiveLink();

            Assert.That(FullView.items.Select(x => x.Kind), Does.Not.Contains(NodeKind.DynamicSubScene));

            var sceneSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystem<SceneSystem>();
            var hash = sceneSystem.GetSceneGUID(Path.ChangeExtension(Path.Combine(TestAssetsDirectory, Scene.name, subSceneName), k_SceneExtension));
            sceneSystem.LoadSceneAsync(hash,
               new SceneSystem.LoadParameters
               {
                   Flags = SceneLoadFlags.NewInstance | SceneLoadFlags.BlockOnStreamIn | SceneLoadFlags.BlockOnImport
               });

            // Forcing update for SceneSystem to run even in edit mode
            World.DefaultGameObjectInjectionWorld.Update();
            yield return UpdateLiveLink();

            var dynamicSubSceneNode = FullView.items.SingleOrDefault(x => x.Kind == NodeKind.DynamicSubScene);
            Assert.That(dynamicSubSceneNode, Is.Not.EqualTo(default(EntityHierarchyNodeId)));
            Assert.That(State.GetNodeName(dynamicSubSceneNode), Is.EqualTo(subSceneName));
            Assert.That(State.HasChildren(dynamicSubSceneNode), Is.True);
        }

        // Creates the basic hierarchy for a single scene with a single subscene.
        // ReSharper disable once UnusedTupleComponentInReturnValue
        static (TestHierarchy.TestNode root, TestHierarchy.TestNode subScene, int nextSceneId, int nextEntityId) CreateBaseHierarchyForSubscene()
        {
            var entityId = 0;
            var sceneId = 0;

            var rootNode = TestHierarchy.CreateRoot();

            rootNode.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),                                  // World Time Entity
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),                                  // SubScene Entity
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));                                 // SceneSection Entity

            var subSceneNode =
                rootNode.AddChild(EntityHierarchyNodeId.FromScene(sceneId++))                  // Main Scene
                                        .AddChild(EntityHierarchyNodeId.FromSubScene(sceneId++)); // SubScene

            return (rootNode, subSceneNode, sceneId, entityId);
        }
    }
}
#endif
