using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;

namespace JY.Toon.DB
{
[AddComponentMenu("Dynamic Bone/Dynamic Bone JY")]
public class DynamicBoneJY : MonoBehaviour
{
    public const int MAX_TRANSFORM_LIMIT = 250;
    public const int MAX_PARTICLE_TREE_LIMIT = 40;

#if UNITY_5_3_OR_NEWER
    [Tooltip("The roots of the transform hierarchy to apply physics.")]
#endif
    public Transform m_Root = null;
    public List<Transform> m_Roots = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("How much the bones slowed down.")]
#endif
    [Range(0, 1)]
    public float m_Damping = 0.1f;
    public AnimationCurve m_DampingDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("How much the force applied to return each bone to original orientation.")]
#endif
    [Range(0, 1)]
    public float m_Elasticity = 0.1f;
    public AnimationCurve m_ElasticityDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("How much bone's original orientation are preserved.")]
#endif
    [Range(0, 1)]
    public float m_Stiffness = 0.1f;
    public AnimationCurve m_StiffnessDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("How much character's position change is ignored in physics simulation.")]
#endif
    [Range(0, 1)]
    public float m_Inert = 0;
    public AnimationCurve m_InertDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("How much the bones slowed down when collide.")]
#endif
    public float m_Friction = 0;
    public AnimationCurve m_FrictionDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("Each bone can be a sphere to collide with colliders. Radius describe sphere's size.")]
#endif
    public float m_Radius = 0;
    public AnimationCurve m_RadiusDistrib = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("If End Length is not zero, an extra bone is generated at the end of transform hierarchy.")]
#endif
    public float m_EndLength = 0;

#if UNITY_5_3_OR_NEWER
    [Tooltip("If End Offset is not zero, an extra bone is generated at the end of transform hierarchy.")]
#endif
    public Vector3 m_EndOffset = Vector3.zero;

#if UNITY_5_3_OR_NEWER
    [Tooltip("The force apply to bones. Partial force apply to character's initial pose is cancelled out.")]
#endif
    public Vector3 m_Gravity = Vector3.zero;

#if UNITY_5_3_OR_NEWER
    [Tooltip("The force apply to bones.")]
#endif
    public Vector3 m_Force = Vector3.zero;

#if UNITY_5_3_OR_NEWER
    [Tooltip("Control how physics blends with existing animation.")]
#endif
    [Range(0, 1)]
    public float m_BlendWeight = 1.0f;

#if UNITY_5_3_OR_NEWER
    [Tooltip("Collider objects interact with the bones.")]
#endif
    public List<DynamicBoneColliderBase> m_Colliders = null;

#if UNITY_5_3_OR_NEWER
    [Tooltip("Bones exclude from physics simulation.")]
#endif
    public List<Transform> m_Exclusions = null;

    public enum FreezeAxis
    {
        None, X, Y, Z
    }
#if UNITY_5_3_OR_NEWER
    [Tooltip("Constrain bones to move on specified plane.")]
#endif	
    public FreezeAxis m_FreezeAxis = FreezeAxis.None;

#if UNITY_5_3_OR_NEWER
    [Tooltip("Disable physics simulation automatically if character is far from camera or player.")]
#endif
    public bool m_DistantDisable = false;
    public Transform m_ReferenceObject = null;
    public float m_DistanceToObject = 20;

    [HideInInspector]
    public bool m_Multithread = true;
    public bool useJob = true;

    Vector3 m_ObjectMove;
    Vector3 m_ObjectPrevPosition;
    float m_ObjectScale;

    float m_Time = 0;
    float m_Weight = 1.0f;
    bool m_DistantDisabled = false;
    int m_PreUpdateCount = 0;
    public Vector3 m_LocalGravity = Vector3.zero;
    public struct HeadInfo
    {
        int m_HeadIndex;
        public float3 m_PerFrameForce;
        public float3 m_ObjectMove;
        public float m_Weight;
        public int m_AllParticleCount;
        public int m_ParticleTreeCount;
        public int m_jobDataOffset;
        public int m_jobTreeDataOffset;
        public float3 m_RootParentBoneWorldPos;
        public quaternion m_RootParentBoneWorldRot;

        public void ResetHeadIndex(int index)
        {
            this.m_HeadIndex = index;
        }

        public int GetHeadIndex()
        {
            return this.m_HeadIndex;
        }
    }
    public struct Particle
    {
        public int index;
        public int m_ParentIndex;
        public int m_ChildCount;
        public float m_Damping;
        public float m_Elasticity;
        public float m_Stiffness;
        public float m_Inert;
        public float m_Friction;
        public float m_Radius;
        public float m_BoneLength;
        public int m_isCollide;
        public int m_TransformNotNull;

        public float3 m_EndOffset;
        public float3 m_InitLocalPosition;
        public quaternion m_InitLocalRotation;


        //for calc worldPos
        public float3 localPosition;
        public quaternion localRotation;

        public float3 tmpWorldPosition;
        public float3 tmpPrevWorldPosition;

        public float3 parentScale;
        public int isRootParticle;

        //for output
        public float3 worldPosition;
        public quaternion worldRotation;
    }
    
    public struct ParticleTree
    {
        public int index;
        public float3 m_LocalGravity;
        public float4x4 m_RootWorldToLocalMatrix;
        public float m_BoneTotalLength;
        public int m_ParticleStartIndex;  // 在全局粒子数组中的起始索引
        public int m_SingleTreeParticleCount;       // 这个树的粒子数量
    }

    // 全局数据
    public NativeArray<ParticleTree> m_ParticleTrees;   // 所有粒子树的数据
    public NativeArray<Particle> m_AllParticles;       // 所有粒子数据
    public Transform[] m_AllTransforms;                 // 所有Transform引用
    public Transform[] m_AllRootParentTransforms;     // 所有根父级Transform引用
    public int m_ParticleTreeCount;
    public int m_AllParticleCount;
    public Transform m_transform;
    public Transform m_rootParentTransform;
    public HeadInfo m_headInfo;
    private Vector3 m_GravityNormalize;

    List<DynamicBoneColliderBase> m_EffectiveColliders;

    private void Awake()
    {
        m_headInfo = new HeadInfo();
        m_headInfo.m_ObjectMove = this.m_ObjectMove;
        m_headInfo.m_Weight = this.m_Weight;
        m_AllParticleCount = 0;

        m_AllParticles = new NativeArray<Particle>(MAX_TRANSFORM_LIMIT, Allocator.Persistent);
        m_ParticleTrees = new NativeArray<ParticleTree>(MAX_PARTICLE_TREE_LIMIT, Allocator.Persistent);
        m_AllTransforms = new Transform[MAX_TRANSFORM_LIMIT];
        m_AllRootParentTransforms = new Transform[MAX_PARTICLE_TREE_LIMIT];
        m_ParticleTreeCount = 0;

        SetupParticles();
    }
    public HeadInfo ResetHeadIndexAndDataOffset(int headIndex)
    {
        m_headInfo.ResetHeadIndex(headIndex);
        m_headInfo.m_jobDataOffset = headIndex * MAX_TRANSFORM_LIMIT;

        return m_headInfo;
    }

    void Update()
    {
        if (useJob)
        {
            return;
        }
    }

    void LateUpdate()
    {
        if (useJob)
        {
            return;
        }
    }

    /* void OnValidate()
    {
        if (Application.isEditor && Application.isPlaying)
        {
            m_UpdateRate = Mathf.Max(m_UpdateRate, 0);
            m_Damping = Mathf.Clamp01(m_Damping);
            m_Elasticity = Mathf.Clamp01(m_Elasticity);
            m_Stiffness = Mathf.Clamp01(m_Stiffness);
            m_Inert = Mathf.Clamp01(m_Inert);
            m_Friction = Mathf.Clamp01(m_Friction);
            m_Radius = Mathf.Max(m_Radius, 0);

            if (IsRootChanged())
            {
                InitTransforms();
                SetupParticles();
            }
            else
            {
                if (!m_AllParticles.IsCreated || !m_ParticleTrees.IsCreated)
                {
                    SetupParticles();
                }
                UpdateParameters();
            }
            DynamicBoneJYManager.Instance.OnUpdate(this);
        }
        
    } */

    bool IsRootChanged()
    {
        var roots = new List<Transform>();
        if (m_Root != null)
        {
            roots.Add(m_Root);
        }

        if (m_Roots != null)
        {
            foreach (var root in m_Roots)
            {
                if (root != null && !roots.Contains(root))
                {
                    roots.Add(root);
                }
            }
        }

        if (roots.Count != m_ParticleTreeCount)
            return true;

        for (int i = 0; i < roots.Count; ++i)
        {
            if (roots[i] != m_AllRootParentTransforms[i])
                return true;
        }

        return false;
    }

    void OnDidApplyAnimationProperties()
    {
        UpdateParameters();
    }

    public void ClearJobData()
    {
        if (m_AllParticles.IsCreated)
        {
            m_AllParticles.Dispose();
        }
        if (m_ParticleTrees.IsCreated)
        {
            m_ParticleTrees.Dispose();
        }
        
        m_AllTransforms = null;
        m_AllRootParentTransforms = null;
    }
    void OnEnable()
    {
        //ResetParticlesPosition(ref m_headInfo);

        DynamicBoneJYManager.Instance.OnEnter(this, ref m_headInfo, this.m_AllParticles, this.m_AllTransforms);
        useJob = true;
    }

    void OnDisable()
    {
        InitTransforms();
        ClearJobData();
        if(DynamicBoneJYManager.Instance != null)
        {
            DynamicBoneJYManager.Instance.OnExit(this, ref m_headInfo);
        }
    }

    public void SetWeight(float w)
    {
        if (m_Weight != w)
        {
            if (w == 0)
            {
                InitTransforms();
            }
            else if (m_Weight == 0)
            {
                ResetParticlesPosition();
            }
            m_Weight = m_BlendWeight = w;
        }
    }

    public float GetWeight()
    {
        return m_Weight;
    }

    public void SetupParticles()
    {
        m_transform = this.transform;

        // 还原重力和外力影响
        Vector3 rf = Vector3.zero;

        if (m_Root != null)
        {
            AppendParticleTree(m_Root);
            m_rootParentTransform = m_Root.parent;

            // 还原重力和外力影响
            m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);
            rf = m_Root.TransformDirection(m_LocalGravity);
        }

        if (m_Roots != null)
        {
            
            if(m_rootParentTransform==null)
            {
                m_rootParentTransform = m_Roots[0].parent;

                // 还原重力和外力影响
                m_LocalGravity = m_Roots[0].InverseTransformDirection(m_Gravity);
                rf = m_Roots[0].TransformDirection(m_LocalGravity);
            }

            for (int i = 0; i < m_Roots.Count; ++i)
            {
                Transform root = m_Roots[i];
                if (root == null)
                    continue;

                bool exists = false;
                for (int j = 0; j < MAX_PARTICLE_TREE_LIMIT; j++)
                {
                    if (m_AllRootParentTransforms[j] == root.parent)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    continue;
                }

                AppendParticleTree(root);
            }
        }

        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
        m_ObjectPrevPosition = transform.position;
        m_ObjectMove = Vector3.zero;

        // 还原重力和外力影响
        m_GravityNormalize = m_Gravity.normalized;
        Vector3 force = m_Gravity;
        Vector3 fdir = m_GravityNormalize;
        Vector3 pf = fdir * Mathf.Max(Vector3.Dot(rf, fdir), 0);	// project current gravity to rest gravity
        force -= pf;	// remove projected gravity
        force = (force + m_Force) * m_ObjectScale;
        m_headInfo.m_PerFrameForce = force;

        for (int i = 0; i < m_ParticleTreeCount; ++i)
        {
            ParticleTree pt = m_ParticleTrees[i];
            pt.m_ParticleStartIndex = m_AllParticleCount;
            Transform root = m_AllRootParentTransforms[i];
            
            AppendParticles(ref pt, root, -1, 0);

            m_ParticleTrees[i] = pt;
        }

        UpdateParameters();

        m_headInfo.m_AllParticleCount = m_AllParticleCount;
        m_headInfo.m_ParticleTreeCount = m_ParticleTreeCount;
    }

    void AppendParticleTree(Transform root)
    {
        if (root == null)
            return;
        
        var pt = new ParticleTree();
        pt.index = m_ParticleTreeCount++;
        //pt.m_Root = root;
        pt.m_RootWorldToLocalMatrix = root.worldToLocalMatrix;

        m_ParticleTrees[pt.index] = pt;
        m_AllRootParentTransforms[pt.index] = root;
    }

    void AppendParticles(ref ParticleTree pt, Transform b, int parentIndex, float boneLength)
    {
        var p = new Particle();
        //p.m_Transform = b;

        pt.m_SingleTreeParticleCount++; // 累加每个子树上粒子数量
        p.index = m_AllParticleCount++; // 累加总粒子数量

        p.m_ParentIndex = parentIndex;

        if (b != null)
        {
            p.m_TransformNotNull = 1;
            p.m_InitLocalPosition = b.localPosition;
            p.m_InitLocalRotation = b.localRotation;

            //extend

            p.localPosition = b.localPosition;
            p.localRotation = b.localRotation;
            p.tmpWorldPosition = p.tmpPrevWorldPosition = b.position;

            p.worldPosition = b.position;
            p.worldRotation = b.rotation;

            p.parentScale = b.parent.lossyScale;
            p.isRootParticle = parentIndex == -1 ? 1 : 0;
        }
        else 	// end bone
        {
            p.m_TransformNotNull = 0;
            Transform pb = m_AllTransforms[parentIndex];
            if (m_EndLength > 0)
            {
                Transform ppb = pb.parent;
                if (ppb != null)
                    p.m_EndOffset = pb.InverseTransformPoint((pb.position * 2 - ppb.position)) * m_EndLength;
                else
                    p.m_EndOffset = new float3(m_EndLength, 0, 0);
            }
            else
            {
                p.m_EndOffset = pb.InverseTransformPoint(transform.TransformDirection(m_EndOffset) + pb.position);
            }
            //p.m_Position = p.m_PrevPosition = pb.TransformPoint(p.m_EndOffset);
            p.parentScale = pb.lossyScale;
            p.tmpWorldPosition = p.tmpPrevWorldPosition = pb.TransformPoint(p.m_EndOffset);

        }

        if (parentIndex >= 0)
        {
            float dis = math.distance(m_AllTransforms[parentIndex].position, p.tmpWorldPosition);
            boneLength += dis;
            p.m_BoneLength = boneLength;
            pt.m_BoneTotalLength = Mathf.Max(pt.m_BoneTotalLength, boneLength);
            //++pt.m_Particles[parentIndex].m_ChildCount;
        }
        int index = p.index;
        m_AllParticles[p.index] = p;
        m_AllTransforms[p.index] = b;
        //pt.m_Particles.Add(p);

        if (b != null)
        {
            for (int i = 0; i < b.childCount; ++i)
            {
                Transform child = b.GetChild(i);
                bool exclude = false;
                if (m_Exclusions != null)
                {
                    exclude = m_Exclusions.Contains(child);
                }
                if (!exclude)
                {
                    AppendParticles(ref pt, child, index, boneLength);
                }
                else if (m_EndLength > 0 || m_EndOffset != Vector3.zero)
                {
                    AppendParticles(ref pt, null, index, boneLength);
                }
            }

            if (b.childCount == 0 && (m_EndLength > 0 || m_EndOffset != Vector3.zero))
            {
                AppendParticles(ref pt, null, index, boneLength);
            }
        }
    }

    public void UpdateParameters()
    {
        SetWeight(m_BlendWeight);

        for (int i = 0; i < m_ParticleTreeCount; ++i)
        {
            UpdateParameters(m_ParticleTrees[i]);
        }
    }

    void UpdateParameters(ParticleTree pt)
    {
        float3 temp_LocalGravity = math.mul(pt.m_RootWorldToLocalMatrix, new float4(m_Gravity, 0)).xyz;
        pt.m_LocalGravity = math.normalize(temp_LocalGravity) * math.length(m_Gravity);

        for (int i = 0; i < m_AllParticleCount; ++i)
        {
            Particle p = m_AllParticles[i];
            p.m_Damping = m_Damping;
            p.m_Elasticity = m_Elasticity;
            p.m_Stiffness = m_Stiffness;
            p.m_Inert = m_Inert;
            p.m_Friction = m_Friction;
            p.m_Radius = m_Radius;

            if (pt.m_BoneTotalLength > 0)
            {
                float a = p.m_BoneLength / pt.m_BoneTotalLength;
                if (m_DampingDistrib != null && m_DampingDistrib.keys.Length > 0)
                    p.m_Damping *= m_DampingDistrib.Evaluate(a);
                if (m_ElasticityDistrib != null && m_ElasticityDistrib.keys.Length > 0)
                    p.m_Elasticity *= m_ElasticityDistrib.Evaluate(a);
                if (m_StiffnessDistrib != null && m_StiffnessDistrib.keys.Length > 0)
                    p.m_Stiffness *= m_StiffnessDistrib.Evaluate(a);
                if (m_InertDistrib != null && m_InertDistrib.keys.Length > 0)
                    p.m_Inert *= m_InertDistrib.Evaluate(a);
                if (m_FrictionDistrib != null && m_FrictionDistrib.keys.Length > 0)
                    p.m_Friction *= m_FrictionDistrib.Evaluate(a);
                if (m_RadiusDistrib != null && m_RadiusDistrib.keys.Length > 0)
                    p.m_Radius *= m_RadiusDistrib.Evaluate(a);
            }

            p.m_Damping = Mathf.Clamp01(p.m_Damping);
            p.m_Elasticity = Mathf.Clamp01(p.m_Elasticity);
            p.m_Stiffness = Mathf.Clamp01(p.m_Stiffness);
            p.m_Inert = Mathf.Clamp01(p.m_Inert);
            p.m_Friction = Mathf.Clamp01(p.m_Friction);
            p.m_Radius = Mathf.Max(p.m_Radius, 0);

            m_AllParticles[i] = p;
        }
    }

    void InitTransforms()
    {
        for (int i = 0; i < m_ParticleTreeCount; ++i)
        {
            InitTransforms(m_ParticleTrees[i]);
        }
    }

    void InitTransforms(ParticleTree pt)
    {
        for (int i = 0; i < m_AllParticleCount; ++i)
        {
            Particle p = m_AllParticles[i];
            if (p.m_TransformNotNull == 1)
            {
                p.localPosition = p.m_InitLocalPosition;
                p.localRotation = p.m_InitLocalRotation;
            }
        }
    }

    void ResetParticlesPosition()
    {
        for (int i = 0; i < m_ParticleTreeCount; ++i)
        {
            ResetParticlesPosition(m_ParticleTrees[i]);
        }

        m_ObjectPrevPosition = transform.position;
    }

    void ResetParticlesPosition(ParticleTree pt)
    {
        m_ObjectPrevPosition = m_transform.position;
    }
}
}