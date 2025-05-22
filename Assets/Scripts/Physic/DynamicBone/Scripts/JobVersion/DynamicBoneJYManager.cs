using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
namespace JY.Toon.DB
{
    public class DynamicBoneJYManager : MonoBehaviour
    {
        public bool EnableGizmos = false;
        
        [Tooltip("物理更新频率")]
        public float m_UpdateRate = 60.0f;
        private float m_Time = 0;
        
        public enum UpdateMode
        {
            Normal,      // 基于真实时间更新
            Default,     // 基于帧数更新
            UnscaledTime // 使用非缩放时间
        }
        [Tooltip("物理更新模式")]
        public UpdateMode m_UpdateMode = UpdateMode.Normal;
        
        private static DynamicBoneJYManager m_instance;

        public static DynamicBoneJYManager Instance
        {
            get
            {
                if(m_instance == null)
                {
                    m_instance = GameObject.FindObjectOfType<DynamicBoneJYManager>();

                    if(m_instance != null)
                    {
                        m_instance.Init();
                    }
                }

                return m_instance;
            }
        }


        // 让子物体不受unity transform的影响
        [BurstCompile]
        struct RootPosApplyJob : IJobParallelForTransform
        {
            public NativeArray<DynamicBoneJY.HeadInfo> ParticleHeadInfo;

            public void Execute(int index, TransformAccess transform)
            {
                DynamicBoneJY.HeadInfo headInfo = ParticleHeadInfo[index];
                headInfo.m_RootParentBoneWorldPos = transform.position;
                headInfo.m_RootParentBoneWorldRot = transform.rotation;

                ParticleHeadInfo[index] = headInfo;
            }
        }

        [BurstCompile]
        struct PrepareParticleJob : IJob
        {
            [ReadOnly]
            public NativeArray<DynamicBoneJY.ParticleTree> ParticleTreeInfo;
            [ReadOnly]
            public NativeArray<DynamicBoneJY.HeadInfo> ParticleHeadInfo;
            public NativeArray<DynamicBoneJY.Particle> ParticleInfo;
            public int HeadCount;

            public void Execute()
            {
                for (int i = 0; i < HeadCount; i++)
                {
                    DynamicBoneJY.HeadInfo curHeadInfo = ParticleHeadInfo[i];
                    for (int k = 0; k < curHeadInfo.m_ParticleTreeCount; k++)
                    {
                        int ptIdx = curHeadInfo.m_jobTreeDataOffset + k;
                        DynamicBoneJY.ParticleTree pt = ParticleTreeInfo[ptIdx];
                        float3 parentPosition = curHeadInfo.m_RootParentBoneWorldPos;
                        quaternion parentRotation = curHeadInfo.m_RootParentBoneWorldRot;

                        int particleOffset = pt.m_ParticleStartIndex;
                        for (int j = 0; j < pt.m_SingleTreeParticleCount; j++)
                        {
                            int pIdx = curHeadInfo.m_jobDataOffset + particleOffset + j;
                            DynamicBoneJY.Particle p = ParticleInfo[pIdx];
                            
                            var localPosition = p.localPosition * p.parentScale;
                            var localRotation = p.localRotation;
                            var worldPosition = parentPosition + math.mul(parentRotation, localPosition);
                            var worldRotation = math.mul(parentRotation, localRotation);

                            p.worldPosition = worldPosition;
                            p.worldRotation = worldRotation;

                            parentPosition = worldPosition;
                            parentRotation = worldRotation;

                            ParticleInfo[pIdx] = p;
                        }
                    }
                }
            }
        }
        
        [BurstCompile]
        struct UpdateParticles1Job : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<DynamicBoneJY.ParticleTree> ParticleTreeInfo;
            [ReadOnly]
            public NativeArray<DynamicBoneJY.HeadInfo> ParticleHeadInfo;
            public NativeArray<DynamicBoneJY.Particle> ParticleInfo;
            public int HeadCount;
            public float TimeVar;

            public void Execute(int index)
            {
                int headIndex = index / DynamicBoneJY.MAX_TRANSFORM_LIMIT;
                DynamicBoneJY.HeadInfo curHeadInfo = ParticleHeadInfo[headIndex];

                int singleId = index % DynamicBoneJY.MAX_TRANSFORM_LIMIT;
                if (singleId >= curHeadInfo.m_AllParticleCount) return;

                int pIdx = curHeadInfo.m_jobDataOffset + singleId;

                DynamicBoneJY.Particle p = ParticleInfo[pIdx];

                if (p.m_ParentIndex >= 0)
                {
                    float3 ev = p.tmpWorldPosition - p.tmpPrevWorldPosition;
                    float3 evrmove = curHeadInfo.m_ObjectMove * p.m_Inert;
                    p.tmpPrevWorldPosition = p.tmpWorldPosition + evrmove;

                    float edamping = p.m_Damping;
                    if (p.m_isCollide == 1)
                    {
                        edamping += p.m_Friction;
                        if (edamping > 1)
                            edamping = 1;
                        p.m_isCollide = 0;
                    }

                    float3 eForce = curHeadInfo.m_PerFrameForce * TimeVar;
                    float3 tmp = ev * (1 - edamping) + eForce + evrmove;
                    p.tmpWorldPosition += tmp;
                }
                else
                {
                    p.tmpPrevWorldPosition = p.tmpWorldPosition;
                    p.tmpWorldPosition = p.worldPosition;
                }

                ParticleInfo[pIdx] = p;
            }
        }

        [BurstCompile]
        struct UpdateParticle2Job : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<DynamicBoneJY.ParticleTree> ParticleTreeInfo;
            [ReadOnly]
            public NativeArray<DynamicBoneJY.HeadInfo> ParticleHeadInfo;
            public NativeArray<DynamicBoneJY.Particle> ParticleInfo;
            public int HeadCount;
            public float TimeVar;

            public void Execute(int index)
            {
                {
                    int headIndex = index / DynamicBoneJY.MAX_TRANSFORM_LIMIT;
                    DynamicBoneJY.HeadInfo curHeadInfo = ParticleHeadInfo[headIndex];

                    // 跳过所有粒子树根节点index
                    for (int i = 0; i < DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT; i++)
                    {
                        int ptIdx = curHeadInfo.m_jobTreeDataOffset + i;
                        if (index % DynamicBoneJY.MAX_TRANSFORM_LIMIT == ParticleTreeInfo[ptIdx].m_ParticleStartIndex)
                        {
                            return;
                        }
                    }
                    
                    {
                        int singleId = index % DynamicBoneJY.MAX_TRANSFORM_LIMIT;

                        if (singleId >= curHeadInfo.m_AllParticleCount) return;

                        int pIdx = curHeadInfo.m_jobDataOffset + (index % DynamicBoneJY.MAX_TRANSFORM_LIMIT);

                        DynamicBoneJY.Particle p = ParticleInfo[pIdx];
                        int p0Idx = curHeadInfo.m_jobDataOffset + p.m_ParentIndex;
                        DynamicBoneJY.Particle p0 = ParticleInfo[p0Idx];

                        float3 ePos = p.worldPosition;
                        float3 ep0Pos = p0.worldPosition;

                        float erestLen;
                        if (p.m_TransformNotNull == 1)
                        {
                            erestLen = math.distance(ep0Pos, ePos);
                        }
                        else
                        {
                            float4x4 localToWorld = float4x4.TRS(p0.tmpWorldPosition, p0.worldRotation, p.parentScale);
                            float3 worldEndOffset = math.mul(localToWorld, new float4(p.m_EndOffset, 0)).xyz;
                            erestLen = math.length(worldEndOffset);
                        }

                        float stiffness = Mathf.Lerp(1.0f, p.m_Stiffness, curHeadInfo.m_Weight);
                        if (stiffness > 0 || p.m_Elasticity > 0)
                        {
                            float4x4 em0 = float4x4.TRS(p0.tmpWorldPosition, p0.worldRotation, p.parentScale);
                            float3 erestPos;
                            if (p.m_TransformNotNull == 1)
                            {
                                erestPos = math.mul(em0, new float4(p.localPosition.xyz, 1)).xyz;
                            }
                            else
                            {
                                erestPos = math.mul(em0, new float4(p.m_EndOffset, 1)).xyz;
                            }
                            
                            float3 ed = erestPos - p.tmpWorldPosition;
                            float3 eStepElasticity = ed * (p.m_Elasticity * TimeVar);
                            p.tmpWorldPosition += eStepElasticity;

                            if (stiffness > 0)
                            {
                                float len = math.distance(erestPos, p.tmpWorldPosition);
                                float maxlen = erestLen * (1 - stiffness) * 2;
                                if (len > maxlen)
                                {
                                    float3 max = ed * ((len - maxlen) / len);
                                    p.tmpWorldPosition += max;
                                }
                            }
                        }

                        float3 edd = p0.tmpWorldPosition - p.tmpWorldPosition;
                        float eleng = math.distance(p0.tmpWorldPosition, p.tmpWorldPosition);
                        if (eleng > 0)
                        {
                            float3 tmp = edd * ((eleng - erestLen) / eleng);
                            p.tmpWorldPosition += tmp;
                        }

                        ParticleInfo[pIdx] = p;
                    }
                }
            }
        }

        [BurstCompile]
        struct ApplyParticleToTransform : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<DynamicBoneJY.ParticleTree> ParticleTreeInfo;
            [ReadOnly]
            public NativeArray<DynamicBoneJY.HeadInfo> ParticleHeadInfo;
            public NativeArray<DynamicBoneJY.Particle> ParticleInfo;
            public int HeadCount;

            public void Execute(int index)
            {
                {
                    int headIndex = index / DynamicBoneJY.MAX_TRANSFORM_LIMIT;
                    DynamicBoneJY.HeadInfo curHeadInfo = ParticleHeadInfo[headIndex];

                    // 跳过所有粒子树根节点index
                    for (int i = 0; i < DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT; i++)
                    {
                        int ptIdx = curHeadInfo.m_jobTreeDataOffset + i;
                        if (index % DynamicBoneJY.MAX_TRANSFORM_LIMIT == ParticleTreeInfo[ptIdx].m_ParticleStartIndex)
                        {
                            return;
                        }
                    }

                    
                    {
                        int singleId = index % DynamicBoneJY.MAX_TRANSFORM_LIMIT;

                        if (singleId >= curHeadInfo.m_AllParticleCount) return;

                        int pIdx = curHeadInfo.m_jobDataOffset + singleId;
    
                        DynamicBoneJY.Particle p = ParticleInfo[pIdx];
                        int p0Idx = curHeadInfo.m_jobDataOffset + p.m_ParentIndex;
                        DynamicBoneJY.Particle p0 = ParticleInfo[p0Idx];

                        if (p0.m_ChildCount <= 1)
                        {
                            float3 ev;
                            if (p.m_TransformNotNull == 1)
                            {
                                ev = p.localPosition;
                            }
                            else
                            {
                                ev = p.m_EndOffset;
                            }
                            float3 ev2 = p.tmpWorldPosition - p0.tmpWorldPosition;

                            var worldV = math.mul(p0.worldRotation, ev).xyz;    // 用来求角度所以只还原世界空间旋转
                            Quaternion erot = Quaternion.FromToRotation(worldV, ev2);
                            var eoutputRot = math.mul(erot, p0.worldRotation);
                            p0.worldRotation = eoutputRot;
                        }

                        p.worldPosition = p.tmpWorldPosition;

                        ParticleInfo[pIdx] = p;
                        ParticleInfo[p0Idx] = p0;
                    }
                }
            }
        }

        // 应用transform
        [BurstCompile]
        struct FinalJob : IJobParallelForTransform
        {
            [ReadOnly]
            public NativeArray<DynamicBoneJY.Particle> ParticleInfo;

            public void Execute(int index, TransformAccess transform)
            {
                transform.rotation = ParticleInfo[index].worldRotation;
                transform.position = ParticleInfo[index].worldPosition;
            }
        }
        private NativeList<DynamicBoneJY.ParticleTree> m_particleTreeInfo;

        private List<DynamicBoneJY> m_dynamicBoneList;
        private NativeList<DynamicBoneJY.Particle> m_particleInfo;
        private NativeList<DynamicBoneJY.HeadInfo> m_headInfo;


        private TransformAccessArray m_headRootTransform;
        private TransformAccessArray m_particleTransformArr;
        private int m_DbDataLen = 0;
        private JobHandle m_lastJobHandle;

        private Queue<DynamicBoneJY> m_loadingQueue = new Queue<DynamicBoneJY>();
        private Queue<DynamicBoneJY> m_removeQueue = new Queue<DynamicBoneJY>();
        private Queue<DynamicBoneJY> m_updateQueue = new Queue<DynamicBoneJY>();

        private void Awake()
        {
            if (m_instance == null)
            {
                m_instance = this;
                m_instance.Init();
            }
            else if (m_instance != this)
            {
                Destroy(gameObject);
            }
        }

        public void Init()
        {
            // 防止内存泄漏
            if (m_particleTreeInfo.IsCreated)
                m_particleTreeInfo.Dispose();
                
            if (m_particleInfo.IsCreated)
                m_particleInfo.Dispose();
                
            if (m_headInfo.IsCreated)
                m_headInfo.Dispose();
                
            if (m_particleTransformArr.isCreated)
                m_particleTransformArr.Dispose();
                
            if (m_headRootTransform.isCreated)
                m_headRootTransform.Dispose();
                
            m_particleTreeInfo = new NativeList<DynamicBoneJY.ParticleTree>(Allocator.Persistent);// 粒子树

            m_dynamicBoneList = new List<DynamicBoneJY>();
            m_particleInfo = new NativeList<DynamicBoneJY.Particle>(Allocator.Persistent);// 粒子总数
            m_headInfo = new NativeList<DynamicBoneJY.HeadInfo>(Allocator.Persistent);
            m_particleTransformArr = new TransformAccessArray(200 * DynamicBoneJY.MAX_TRANSFORM_LIMIT, 64);
            m_headRootTransform = new TransformAccessArray(200, 64);
        }

        // 每帧更新 维护DynamicBoneJY队列
        void UpdateQueue()
        {
            // 参数变化时更新对应的DynamicBoneJY成员 TODO：暂时不支持动态增删根节点
            while(m_updateQueue.Count > 0)
            {
                DynamicBoneJY target = m_updateQueue.Dequeue();
                int idx = m_dynamicBoneList.IndexOf(target);
                if (idx != -1)
                {
                    int curHeadIndex = target.m_headInfo.GetHeadIndex();
                    // 更新 HeadInfo
                    m_headInfo[idx] = target.m_headInfo;
                    
                    // 更新粒子数据 - 使用实际的粒子数量
                    for (int i = 0; i < DynamicBoneJY.MAX_TRANSFORM_LIMIT; i++)
                    {
                        int pOffset = curHeadIndex * DynamicBoneJY.MAX_TRANSFORM_LIMIT + i;
                        m_particleInfo[pOffset] = target.m_AllParticles[i];
                    }
                    
                    // 更新树信息 - 使用实际的树数量
                    for (int i = 0; i < DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT; i++)
                    {
                        int ptOffset = curHeadIndex * DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT + i;
                        m_particleTreeInfo[ptOffset] = target.m_ParticleTrees[i];
                    }
                    
                    // 更新根节点
                    m_headRootTransform[idx] = target.m_rootParentTransform;
                }
            }
            
            while(m_loadingQueue.Count > 0)
            {
                DynamicBoneJY target = m_loadingQueue.Dequeue();

                int idx = m_dynamicBoneList.IndexOf(target);
                if (idx == -1)
                {
                    m_dynamicBoneList.Add(target);

                    target.m_headInfo.m_jobDataOffset = m_particleInfo.Length;
                    target.m_headInfo.m_jobTreeDataOffset = m_particleTreeInfo.Length;

                    int headIndex = m_headInfo.Length;
                    target.m_headInfo.ResetHeadIndex(headIndex);

                    m_headInfo.Add(target.m_headInfo);
                    m_particleInfo.AddRange(target.m_AllParticles);
                    m_headRootTransform.Add(target.m_rootParentTransform);

                    m_particleTreeInfo.AddRange(target.m_ParticleTrees);

                    for (int i = 0; i < DynamicBoneJY.MAX_TRANSFORM_LIMIT; i++)
                    {
                        m_particleTransformArr.Add(target.m_AllTransforms[i]);
                    }

                    m_DbDataLen++;
                }
            }

            while(m_removeQueue.Count > 0)
            {
                DynamicBoneJY target = m_removeQueue.Dequeue();

                int idx = m_dynamicBoneList.IndexOf(target);
                if (idx != -1)
                {
                    m_dynamicBoneList.RemoveAt(idx);

                    int curHeadIndex = target.m_headInfo.GetHeadIndex();

                    // 是否是队列中末尾对象
                    bool isEndTarget = curHeadIndex == m_headInfo.Length - 1;
                    if (isEndTarget)
                    {
                        m_headInfo.RemoveAtSwapBack(curHeadIndex);
                        m_headRootTransform.RemoveAtSwapBack(curHeadIndex);

                        for (int i = DynamicBoneJY.MAX_TRANSFORM_LIMIT - 1; i >= 0; i--)
                        {
                            int dataOffset = curHeadIndex * DynamicBoneJY.MAX_TRANSFORM_LIMIT + i;
                            m_particleInfo.RemoveAtSwapBack(dataOffset);
                            m_particleTransformArr.RemoveAtSwapBack(dataOffset);
                        }
                        for (int i = DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT - 1; i >= 0; i--)
                        {
                            int dataOffset = curHeadIndex * DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT + i;
                            m_particleTreeInfo.RemoveAtSwapBack(dataOffset);
                        }
                    }
                    else
                    {
                        // 将最末列的HeadInfo 索引设置为当前将要移除的HeadInfo 索引
                        DynamicBoneJY lastTarget = m_dynamicBoneList[m_dynamicBoneList.Count - 1];

                        DynamicBoneJY.HeadInfo lastHeadInfo = lastTarget.ResetHeadIndexAndDataOffset(curHeadIndex);

                        m_headInfo.RemoveAtSwapBack(curHeadIndex);

                        m_headInfo[curHeadIndex] = lastHeadInfo;

                        m_headRootTransform.RemoveAtSwapBack(curHeadIndex);

                        for (int i = DynamicBoneJY.MAX_TRANSFORM_LIMIT - 1; i >= 0; i--)
                        {
                            int dataOffset = curHeadIndex * DynamicBoneJY.MAX_TRANSFORM_LIMIT + i;
                            m_particleInfo.RemoveAtSwapBack(dataOffset);
                            m_particleTransformArr.RemoveAtSwapBack(dataOffset);
                        }
                        for (int i = DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT - 1; i >= 0; i--)
                        {
                            int dataOffset = curHeadIndex * DynamicBoneJY.MAX_PARTICLE_TREE_LIMIT + i;
                            m_particleTreeInfo.RemoveAtSwapBack(dataOffset);
                        }
                    }

                    m_DbDataLen--;
                }

                target.ClearJobData();
            }
        }
        // DynamicBoneJY注册
        public void OnEnter(DynamicBoneJY target, ref DynamicBoneJY.HeadInfo headInfo, NativeArray<DynamicBoneJY.Particle> particleInfo, Transform[] particleTransformList)
        {
            m_loadingQueue.Enqueue(target);
        }
        // DynamicBoneJY注销
        public void OnExit(DynamicBoneJY target, ref DynamicBoneJY.HeadInfo headInfo)
        {
            m_removeQueue.Enqueue(target);
        }

        public void OnUpdate(DynamicBoneJY target)
        {
            m_updateQueue.Enqueue(target);
        }

        private void Update()
        {
            if (m_DbDataLen == 0)
            {
                return;
            }
        }

        private void LateUpdate()
        {
            if (!m_lastJobHandle.IsCompleted)
            {
                return;
            }

            m_lastJobHandle.Complete();

            UpdateQueue();

            if (m_DbDataLen == 0)
            {
                return;
            }

            // 控制物理计算更新频率
            float dt = Time.deltaTime;
            if (m_UpdateMode == UpdateMode.UnscaledTime)
            {
                dt = Time.unscaledDeltaTime;
            }

            // 计算物理更新参数
            int loop = 1;
            float timeVar = 1.0f;
            bool needsUpdate = true;

            if (m_UpdateMode == UpdateMode.Default)
            {
               // 在Default模式下，timeVar控制更新步长
               if (m_UpdateRate > 0)
               {
                   timeVar = dt * m_UpdateRate;
               }
            }
            else
            {
                // 基于真实时间的物理更新
                if (m_UpdateRate > 0)
                {
                    float frameTime = 1.0f / m_UpdateRate;
                    m_Time += dt;
                    loop = 0;
                    
                    // 根据时间间隔决定执行物理模拟的次数
                    while (m_Time >= frameTime)
                    {
                        m_Time -= frameTime;
                        if (++loop >= 3)
                        {
                            m_Time = 0;
                            break;
                        }
                    }
                    
                    // 如果没有累积足够的时间，则跳过更新
                    if (loop <= 0)
                    {
                        needsUpdate = false;
                    }
                }
            }
            
            // 如果不需要更新，则跳过
            if (!needsUpdate)
            {
                return;
            }

            var dataArrLength = m_DbDataLen * DynamicBoneJY.MAX_TRANSFORM_LIMIT;

            var rootJob = new RootPosApplyJob
            {
                ParticleHeadInfo = this.m_headInfo
            };
            var rootHandle = rootJob.Schedule(m_headRootTransform);

            var prepareJob = new PrepareParticleJob
            {
                ParticleHeadInfo = this.m_headInfo,
                ParticleInfo = this.m_particleInfo,
                ParticleTreeInfo = this.m_particleTreeInfo,
                HeadCount = m_DbDataLen
            };
            var prepareHandle = prepareJob.Schedule(rootHandle);
            
            var handle = prepareHandle;
            
            // 根据loop次数执行物理更新
            for (int i = 0; i < loop; i++)
            {
                var update1Job = new UpdateParticles1Job
                {
                    ParticleHeadInfo = this.m_headInfo,
                    ParticleInfo = this.m_particleInfo,
                    ParticleTreeInfo = this.m_particleTreeInfo,
                    HeadCount = m_DbDataLen,
                    TimeVar = timeVar
                };
                var update1Handle = update1Job.Schedule(dataArrLength, DynamicBoneJY.MAX_TRANSFORM_LIMIT, handle);
                
                var update2Job = new UpdateParticle2Job
                {
                    ParticleHeadInfo = this.m_headInfo,
                    ParticleInfo = this.m_particleInfo,
                    ParticleTreeInfo = this.m_particleTreeInfo,
                    HeadCount = m_DbDataLen,
                    TimeVar = timeVar
                };
                handle = update2Job.Schedule(dataArrLength, DynamicBoneJY.MAX_TRANSFORM_LIMIT, update1Handle);
            }
            
            var appTransJob = new ApplyParticleToTransform
            {
                ParticleHeadInfo = this.m_headInfo,
                ParticleInfo = this.m_particleInfo,
                ParticleTreeInfo = this.m_particleTreeInfo,
                HeadCount = m_DbDataLen
            };

            var appTransHandle = appTransJob.Schedule(dataArrLength, DynamicBoneJY.MAX_TRANSFORM_LIMIT, handle);
            
            var finalJob = new FinalJob
            {
                ParticleInfo = this.m_particleInfo,
            };
            var finalHandle = finalJob.Schedule(this.m_particleTransformArr, appTransHandle);

            m_lastJobHandle = finalHandle;

            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>
        /// 选中时绘制Gizmos
        /// </summary>
        void OnDrawGizmos()
        {
            if (!EnableGizmos || m_DbDataLen == 0 || 
                !m_particleInfo.IsCreated || !m_particleTreeInfo.IsCreated || !m_headInfo.IsCreated)
                return;
                
            // 避免线程冲突
            m_lastJobHandle.Complete();
            
            Gizmos.color = Color.white;

            for (int i = 0; i < m_DbDataLen; i++)
            {
                if (i >= m_headInfo.Length)
                    continue;
                    
                DynamicBoneJY.HeadInfo curHeadInfo = m_headInfo[i];
                for (int k = 0; k < curHeadInfo.m_ParticleTreeCount; k++)
                {
                    int ptIdx = curHeadInfo.m_jobTreeDataOffset + k;
                    DynamicBoneJY.ParticleTree pt = m_particleTreeInfo[ptIdx];
                    DrawGizmos(pt, curHeadInfo, i);
                }
            }
        }
        
        void DrawGizmos(DynamicBoneJY.ParticleTree pt, DynamicBoneJY.HeadInfo curHeadInfo, int headIndex)
        {
            if (pt.m_SingleTreeParticleCount <= 0 || !m_particleTransformArr.isCreated)
                return;
                
            for (int i = 0; i < pt.m_SingleTreeParticleCount; i++)
            {
                int pIdx = curHeadInfo.m_jobDataOffset + pt.m_ParticleStartIndex + i;
                DynamicBoneJY.Particle p = m_particleInfo[pIdx];

                if (p.m_ParentIndex >= 0)
                {
                    int p0Idx = curHeadInfo.m_jobDataOffset + p.m_ParentIndex;
                    DynamicBoneJY.Particle p0 = m_particleInfo[p0Idx];
                    Gizmos.DrawLine(p.tmpWorldPosition, p0.tmpWorldPosition);
                }
                
                // 添加半径球体绘制
                if (p.m_Radius > 0)
                {
                    float objectScale = Mathf.Abs(m_headRootTransform[headIndex].lossyScale.x);
                    Gizmos.DrawWireSphere(p.tmpWorldPosition, p.m_Radius * objectScale);
                }
            }
        }
            
        private void OnDestroy()
        {
            // 完成所有job
            m_lastJobHandle.Complete();
            
            if (this.m_particleTransformArr.isCreated)
            {
                this.m_particleTransformArr.Dispose();
            }
            
            if (this.m_particleInfo.IsCreated)
            {
                this.m_particleInfo.Dispose();
            }
            
            if (this.m_headInfo.IsCreated)
            {
                this.m_headInfo.Dispose();
            }
            
            if (this.m_headRootTransform.isCreated)
            {
                this.m_headRootTransform.Dispose();
            }
            
            if (this.m_particleTreeInfo.IsCreated)
            {
                this.m_particleTreeInfo.Dispose();
            }
        }
    }
}