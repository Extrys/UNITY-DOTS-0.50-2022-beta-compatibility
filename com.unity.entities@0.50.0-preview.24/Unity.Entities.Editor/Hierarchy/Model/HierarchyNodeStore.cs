﻿using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// The <see cref="HierarchyNodeStore"/> represents a mutable tree data model to reflect the world hierarchy.
    /// </summary>
    [BurstCompatible]
    unsafe partial struct HierarchyNodeStore : IDisposable
    {
        static readonly HierarchyNodeData k_SharedDefaultEntity = new HierarchyNodeData
        {
            Parent = HierarchyNodeHandle.Root,
            ChangeVersion = -1
        };
        
        /// <summary>
        /// The <see cref="HierarchyNodeData"/> represents the actual data for a given hierarchy node.
        /// </summary>
        struct HierarchyNodeData
        {
            [Flags]
            public enum PackingHint
            {
                ChildrenRequireSorting = 1 << 0
            }
        
            /// <summary>
            /// The last change version for this node.
            /// </summary>
            public int ChangeVersion;

            /// <summary>
            /// The parent handle for this node.
            /// </summary>
            public HierarchyNodeHandle Parent;

            /// <summary>
            /// The sorting key for this node; this is relative to it's siblings.
            /// </summary>
            public int SortIndex;

            /// <summary>
            /// Hint flag to be consumed during the packing step.
            /// </summary>
            public PackingHint PackingHints;
        }
        
        struct HierarchyNodeSortIndexComparer : IComparer<HierarchyNode>
        {
            public int Compare(HierarchyNode x, HierarchyNode y)
            {
                var sortIndexComparison = x.GetSortIndex().CompareTo(y.GetSortIndex());
                return sortIndexComparison != 0 ? sortIndexComparison : x.GetHandle().CompareTo(y.GetHandle());
            }
        }
        
        struct HierarchyNodeStoreData
        {
            public int ChangeVersion;
        }
        
        readonly Allocator m_Allocator;

        [NativeDisableUnsafePtrRestriction] HierarchyNodeStoreData* m_HierarchyNodeStoreData;
        
        /// <summary>
        /// Internal storage for node data. This abstracts the way we get and set node data and allows optimal performance and storage for specific node types.
        /// </summary>
        HierarchyNodeMap<HierarchyNodeData> m_Nodes;
        
        /// <summary>
        /// The children mapping for nodes. 
        /// </summary>
        UnsafeMultiHashMap<HierarchyNodeHandle, HierarchyNodeHandle> m_Children;
        
        /// <summary>
        /// Mapping scene reference entity to the <see cref="UnityEngine.SceneManagement.Scene"/> it belongs to.
        /// </summary>
        NativeHashMap<Entity, Scene> m_SceneReferenceEntityToScene;

        /// <summary>
        /// Returns the current change version for the hierarchy. This value is incremented every time the hierarchy is exported to the immutable set.
        /// </summary>
        int ChangeVersion => m_HierarchyNodeStoreData->ChangeVersion;

        /// <summary>
        /// Initializes a new <see cref="HierarchyNodeStore"/> instance.
        /// </summary>
        /// <param name="allocator">The allocator to use for internal storage.</param>
        public HierarchyNodeStore(Allocator allocator)
        {
            m_Allocator = allocator;
            m_HierarchyNodeStoreData = (HierarchyNodeStoreData*) UnsafeUtility.Malloc(UnsafeUtility.SizeOf<HierarchyNodeStoreData>(), UnsafeUtility.AlignOf<HierarchyNodeStoreData>(), allocator);
            m_HierarchyNodeStoreData->ChangeVersion = 1;
            m_Nodes = new HierarchyNodeMap<HierarchyNodeData>(allocator);
            m_Children = new UnsafeMultiHashMap<HierarchyNodeHandle, HierarchyNodeHandle>(16, allocator);
            m_SceneReferenceEntityToScene = new NativeHashMap<Entity, Scene>(16, allocator);

            m_Nodes.SetSharedDefault(k_SharedDefaultEntity);
        }
        
        public void Dispose()
        {
            UnsafeUtility.Free(m_HierarchyNodeStoreData, m_Allocator);
            m_HierarchyNodeStoreData = null;
            m_Nodes.Dispose();
            m_Children.Dispose();
            m_SceneReferenceEntityToScene.Dispose();
        }

        /// <summary>
        /// Clears the internal data for this hierarchy.
        /// </summary>
        public void Clear()
        {
            m_Nodes.Clear();
            m_Children.Clear();
            m_SceneReferenceEntityToScene.Clear();
            m_HierarchyNodeStoreData->ChangeVersion = 1;
        }
        
        internal int GetRootChangeVersion() 
            => m_Nodes[HierarchyNodeHandle.Root].ChangeVersion;
        
        /// <summary>
        /// Returns the number of valid nodes that exist in the hierarchy.
        /// </summary>
        public int Count()
            => m_Nodes.Count();
        
        /// <summary>
        /// Returns <see langword="true"/> if the given handle exists in the hierarchy.
        /// </summary>
        /// <param name="handle">The handle to check existence for.</param>
        /// <returns><see langword="true"/> if the given handle exists; <see langword="false"/> otherwise.</returns>
        public bool Exists(HierarchyNodeHandle handle)
            => m_Nodes.Exists(handle);

        /// <summary>
        /// Gets the root node for the <see cref="HierarchyNodeStore"/>.
        /// </summary>
        public HierarchyNode GetRoot()
            => new HierarchyNode(this, HierarchyNodeHandle.Root);
        
        /// <summary>
        /// Gets the <see cref="HierarchyNode"/> for the given handle.
        /// </summary>
        public HierarchyNode GetNode(HierarchyNodeHandle handle)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified handle {handle} does not exist in the hierarchy.");
            
            return new HierarchyNode(this, handle);
        }
        
        /// <summary>
        /// Adds a new node to the hierarchy.
        /// </summary>
        /// <param name="handle">The handle for the new node.</param>
        /// <returns>The newly created node.</returns>
        public HierarchyNode AddNode(HierarchyNodeHandle handle)
            => AddNode(handle, HierarchyNodeHandle.Root);

        /// <summary>
        /// Adds a new node to the hierarchy with a specified parent node.
        /// </summary>
        /// <param name="handle">The handle for the new node.</param>
        /// <param name="parent">The parent for the new node.</param>
        /// <returns>The newly created node.</returns>
        public HierarchyNode AddNode(HierarchyNodeHandle handle, HierarchyNodeHandle parent)
        {
            if (handle.Kind == NodeKind.Root)
                throw new InvalidOperationException($"Trying to add {nameof(HierarchyNodeHandle)} with {nameof(NodeKind)}.{nameof(NodeKind.Root)}. This is not allowed.");

            if (m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified handle {handle} already exist in the hierarchy.");
            
            if (!m_Nodes.Exists(parent))
                throw new InvalidOperationException($"The specified handle {parent} does not exist in the hierarchy.");
            
            if (handle == parent)
                throw new InvalidOperationException($"Trying to set the parent for {handle} as itself.");
            
            m_Nodes[handle] = new HierarchyNodeData
            {
                ChangeVersion = ChangeVersion,
                Parent = parent,
                SortIndex = 0
            };

            // Special case; Root entities are not included in the 'm_Children' set and instead handled separately for performance reasons.
            if (!(parent == HierarchyNodeHandle.Root && handle.Kind == NodeKind.Entity))
                m_Children.Add(parent, handle);
            
            UpdateChangeVersion(parent);
            return new HierarchyNode(this, handle);
        }
        
        /// <summary>
        /// Removes the specified node from the hierarchy.
        /// </summary>
        /// <param name="handle">The node to remove.</param>
        public void RemoveNode(HierarchyNodeHandle handle)
        {
            if (handle.Kind == NodeKind.Root)
                throw new InvalidOperationException($"Trying to remove {nameof(HierarchyNodeHandle)} with {nameof(NodeKind)}.{nameof(NodeKind.Root)}. This is not allowed.");

            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");

            var node = m_Nodes[handle];
            
            if (m_Children.TryGetFirstValue(handle, out var childHandle, out var iterator))
            {
                do
                {
                    if (childHandle.Kind == NodeKind.Entity && m_Children.CountValuesForKey(childHandle) == 0)
                    {
                        m_Nodes[childHandle] = k_SharedDefaultEntity;
                    }
                    else
                    {
                        // Update the node parenting.
                        var child = m_Nodes[childHandle];
                        child.Parent = HierarchyNodeHandle.Root;
                        child.ChangeVersion = ChangeVersion;
                        m_Nodes[childHandle] = child;

                        if (childHandle.Kind != NodeKind.Entity)
                        {
                            // Move this node to the root.
                            m_Children.Add(HierarchyNodeHandle.Root, childHandle);
                        }
                    }
                } while (m_Children.TryGetNextValue(out childHandle, ref iterator));
            }
            
            // Update children for the removed node.
            m_Children.Remove(node.Parent, handle);
            m_Children.Remove(handle);

            UpdateChangeVersion(node.Parent);
            m_Nodes.Remove(handle);
        }
        
        /// <summary>
        /// Returns the number of children for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the child count for.</param>
        /// <returns>The number of children for the specified handle.</returns>
        public int GetChildCount(HierarchyNodeHandle handle)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            var count = m_Children.CountValuesForKey(handle);

            // Special case; The 'm_Children' mapping does not track root entities for performance reasons.
            if (handle.Kind == NodeKind.Root)
            {
                foreach (var entityValuePair in m_Nodes.ValueByEntity)
                {
                    if (entityValuePair.Value.Parent.Kind == NodeKind.Root)
                        count++;
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Gets all children for the given <see cref="HierarchyNodeHandle"/> and returns them as a new array.
        /// </summary>
        /// <param name="handle">The handle to get children for.</param>
        /// <returns>A new array containing all child nodes.</returns>
        public HierarchyNode[] GetChildren(HierarchyNodeHandle handle)
        {
            if (!Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");

            var children = new List<HierarchyNode>();
            GetChildren(handle, children);
            return children.ToArray();
        }

        /// <summary>
        /// Gets all children for the given <see cref="HierarchyNodeHandle"/> and adds them to the given <see cref="children"/> list.
        /// </summary>
        /// <param name="handle">The handle to get children for.</param>
        /// <param name="children">The list to add children to.</param>
        public void GetChildren(HierarchyNodeHandle handle, List<HierarchyNode> children)
        {
            if (!Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");

            children.Clear();

            if (m_Children.TryGetFirstValue(handle, out var child, out var iterator))
            {
                do
                {
                    children.Add(new HierarchyNode(this, child));
                } while (m_Children.TryGetNextValue(out child, ref iterator));
            }

            // Special case; The 'm_Children' mapping does not track root entities for performance reasons.
            if (handle.Kind == NodeKind.Root)
            {
                foreach (var entityValuePair in m_Nodes.ValueByEntity)
                {
                    if (entityValuePair.Value.Parent.Kind == NodeKind.Root)
                        children.Add(new HierarchyNode(this, HierarchyNodeHandle.FromEntity(entityValuePair.Entity)));
                }
            }

            if ((m_Nodes[handle].PackingHints & HierarchyNodeData.PackingHint.ChildrenRequireSorting) != 0)
                children.Sort(new HierarchyNodeSortIndexComparer());
        }
        
        /// <summary>
        /// Gets the depth for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the depth for.</param>
        /// <returns>The depth of the specified node.</returns>
        public int GetDepth(HierarchyNodeHandle handle)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            var depth = -1;
            
            for (;;)
            {
                if (handle.Kind <= NodeKind.Root)
                    return depth;
                
                handle = m_Nodes[handle].Parent;
                depth++;
            }
        }
        
        /// <summary>
        /// Gets the sorting index for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the sort index for.</param>
        /// <returns>The sort index for the specified handle.</returns>
        public int GetSortIndex(HierarchyNodeHandle handle)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");

            return m_Nodes[handle].SortIndex;
        }

        /// <summary>
        /// Sets the sorting index for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to set the sort index for.</param>
        /// <param name="index">The value to set.</param>
        public void SetSortIndex(HierarchyNodeHandle handle, int index)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            var node = m_Nodes[handle];
            node.SortIndex = index;
            m_Nodes[handle] = node;

            // Give a hint to the packing system to enable sorting.
            SetPackingHint(node.Parent, HierarchyNodeData.PackingHint.ChildrenRequireSorting);

            UpdateChangeVersion(handle);
        }

        /// <summary>
        /// Gets the parent for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the parent for.</param>
        /// <returns>The parent of the specified node.</returns>
        public HierarchyNodeHandle GetParent(HierarchyNodeHandle handle)
        {
            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            return m_Nodes[handle].Parent;
        }

        /// <summary>
        /// Sets the parent for the specified <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to set the parent for.</param>
        /// <param name="parent">The parent value to set.</param>
        public void SetParent(HierarchyNodeHandle handle, HierarchyNodeHandle parent)
        {
            if (handle.Kind == NodeKind.Root)
                throw new InvalidOperationException($"Trying to set parent for {nameof(HierarchyNodeHandle)} with {nameof(NodeKind)}.{nameof(NodeKind.Root)}. This is not allowed.");

            if (!m_Nodes.Exists(handle))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            if (!m_Nodes.Exists(parent))
                throw new InvalidOperationException($"The specified {handle} does not exist in the hierarchy.");
            
            if (handle == parent)
                throw new InvalidOperationException($"Trying to set the parent for {handle} as itself.");
            
            // @TODO check for cyclical references.
            
            var node = m_Nodes[handle];
            var previousParent = node.Parent;

            m_Children.Remove(node.Parent, handle);
            
            node.Parent = parent;
            node.ChangeVersion = ChangeVersion;
            
            // Special Case; root entities are not included in the 'm_Children' set and instead handled separately.
            if (parent == HierarchyNodeHandle.Root && handle.Kind == NodeKind.Entity)
            {
                if (m_Children.CountValuesForKey(handle) == 0)
                {
                    // This node is transitioning to a root entity with no children. We use a very special path for this.
                    // Since this node has absolutely no hierarchical information associated with it we only need to store it's existence.
                    m_Nodes[handle] = k_SharedDefaultEntity;
                }
                else
                {
                    // Otherwise we need to store this node as normal. 
                    m_Nodes[handle] = node;
                }
            }
            else
            {
                m_Children.Add(parent, handle);
                m_Nodes[handle] = node;
            }

            // Propagate changes up the hierarchy.
            UpdateChangeVersion(previousParent);
            UpdateChangeVersion(parent);
        }

        /// <summary>
        /// Increments the internal change version.
        /// </summary>
        void IncrementChangeVersion()
        {
            m_HierarchyNodeStoreData->ChangeVersion++;
        }

        void UpdateChangeVersion(HierarchyNodeHandle handle)
        {
            for (;;)
            {
                if (handle.Kind == NodeKind.None)
                    return;
                
                var node = m_Nodes[handle];

                if (node.ChangeVersion == ChangeVersion)
                    return;
                
                node.ChangeVersion = ChangeVersion;
                m_Nodes[handle] = node;
                handle = node.Parent;
            }
        }

        void SetPackingHint(HierarchyNodeHandle handle, HierarchyNodeData.PackingHint flag)
        {
            var node = m_Nodes[handle];
            node.PackingHints |= flag;
            m_Nodes[handle] = node;
        }

        void UnsetPackingHint(HierarchyNodeHandle handle, HierarchyNodeData.PackingHint flag)
        {
            var node = m_Nodes[handle];
            node.PackingHints &= ~flag;
            m_Nodes[handle] = node;
        }
    }
}