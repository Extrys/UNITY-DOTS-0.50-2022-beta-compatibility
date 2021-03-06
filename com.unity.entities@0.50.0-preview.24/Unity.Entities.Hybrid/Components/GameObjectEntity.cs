using System;
using System.Collections.Generic;
using Unity.Entities.Conversion;
using UnityEngine;
using UnityEngine.Assertions;
using MonoBehaviour = UnityEngine.MonoBehaviour;
using GameObject = UnityEngine.GameObject;
using Component = UnityEngine.Component;

namespace Unity.Entities
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class GameObjectEntity : MonoBehaviour
    {
        public World World
        {
            get
            {
                if (enabled && gameObject.activeInHierarchy)
                    ReInitializeEntityManagerAndEntityIfNecessary();
                return m_World;
            }
        }

        private World m_World;

        public EntityManager EntityManager
        {
            get
            {
                World w = World;
                if (w != null && w.IsCreated) return w.EntityManager;
                else return default;
            }
        }

        public Entity Entity
        {
            get
            {
                if (enabled && gameObject.activeInHierarchy)
                    ReInitializeEntityManagerAndEntityIfNecessary();
                return m_Entity;
            }
        }
        Entity m_Entity;

        void ReInitializeEntityManagerAndEntityIfNecessary()
        {
            // in case e.g., on a prefab that was open for edit when domain was unloaded
            // existing EntityManager lost all its data, so simply create a new one
            if (m_World != null && !m_World.IsCreated && !m_Entity.Equals(default))
                Initialize();
        }

        static List<Component> s_ComponentsCache = new List<Component>();

        public static Entity AddToEntityManager(EntityManager entityManager, GameObject gameObject)
        {
            var entity = GameObjectConversionMappingSystem.CreateGameObjectEntity(entityManager, gameObject, s_ComponentsCache);
            s_ComponentsCache.Clear();
            return entity;
        }

        //@TODO: is this used? deprecate?
        public static void AddToEntity(EntityManager entityManager, GameObject gameObject, Entity entity)
        {
            var components = gameObject.GetComponents<Component>();

            for (var i = 0; i != components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is GameObjectEntity || component.IsComponentDisabled())
                    continue;

                entityManager.AddComponentObject(entity, component);
            }
        }

        void Initialize()
        {
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                m_World = World.DefaultGameObjectInjectionWorld;
                m_Entity = AddToEntityManager(m_World.EntityManager, gameObject);
            }
        }

        protected virtual void OnEnable()
        {
            Initialize();
        }

        protected virtual void OnDisable()
        {
            if (m_World != null && m_World.IsCreated)
            {
                var em = m_World.EntityManager;
                if (em.Exists(Entity))
                    em.DestroyEntity(Entity);
            }

            m_World = null;
            m_Entity = Entity.Null;
        }
    }
}
