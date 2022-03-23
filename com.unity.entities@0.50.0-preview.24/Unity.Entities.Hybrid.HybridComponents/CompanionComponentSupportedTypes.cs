﻿using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
#if HDRP_7_0_0_OR_NEWER
using UnityEngine.Rendering.HighDefinition;
#endif
#if URP_7_0_0_OR_NEWER
using UnityEngine.Rendering.Universal;
#endif

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid")]
namespace Unity.Entities.Conversion
{
    internal class CompanionComponentSupportedTypes
    {
        public static ComponentType[] Types =
        {
            typeof(Light),
            typeof(LightProbeProxyVolume),
            typeof(ReflectionProbe),
            typeof(TextMesh),
            typeof(MeshRenderer),
            typeof(SpriteRenderer),
            typeof(VisualEffect),
#if PARTICLE_SYSTEM_MODULE
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
#endif
#if SRP_7_0_0_OR_NEWER
            typeof(Volume),
            typeof(SphereCollider),
            typeof(BoxCollider),
            typeof(CapsuleCollider),
            typeof(MeshCollider),
#endif
#if HDRP_7_0_0_OR_NEWER
            typeof(HDAdditionalLightData),
            typeof(HDAdditionalReflectionData),
            typeof(DecalProjector),
            typeof(PlanarReflectionProbe),
            typeof(DensityVolume),
#if PROBEVOLUME_CONVERSION
            typeof(ProbeVolume),
#endif
#endif
#if URP_7_0_0_OR_NEWER
            typeof(UniversalAdditionalLightData),
#endif
#if HYBRID_ENTITIES_CAMERA_CONVERSION
            typeof(Camera),
#if HDRP_7_0_0_OR_NEWER
            typeof(HDAdditionalCameraData),
#endif
#if URP_7_0_0_OR_NEWER
            typeof(UniversalAdditionalCameraData),
#endif
#endif
        };
    }
}
