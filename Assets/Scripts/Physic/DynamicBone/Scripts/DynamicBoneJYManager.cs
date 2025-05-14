using System.Collections;
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
        private static DynamicBoneJYManager m_instance;

        public static DynamicBoneJYManager Instance
        {
            get
            {
                if(null == m_instance)
                {
                    m_instance = GameObject.FindObjectOfType<DynamicBoneJYManager>();
                    if (!m_instance)
                    {
                        GameObject go = new GameObject("DynamicBoneJYManager");
                        m_instance = go.AddComponent<DynamicBoneJYManager>();
                    }
                    m_instance.Init();
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

        // 应用位置信息
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

        private List<DynamicBoneJY> m_dynamicBoneList;
        private NativeList<DynamicBoneJY.Particle> m_particleInfo;
        private NativeList<DynamicBoneJY.HeadInfo> m_headInfo;


        private TransformAccessArray m_headRootTransform;
        private TransformAccessArray m_particleTransformArr;
        private int m_DbDataLen = 0;
        private JobHandle m_lastJobHandle;

        private void Awake()
        {
            if (!m_instance)
            {
                m_instance = this;
                m_instance.Init();
            }
        }

        public void Init()
        {
            m_dynamicBoneList = new List<DynamicBoneJY>();
            m_particleInfo = new NativeList<DynamicBoneJY.Particle>(Allocator.Persistent);
            m_headInfo = new NativeList<DynamicBoneJY.HeadInfo>(Allocator.Persistent);
            m_particleTransformArr = new TransformAccessArray(200 * DynamicBoneJY.MAX_TRANSFORM_LIMIT, 64);
            m_headRootTransform = new TransformAccessArray(200, 64);
        }
        private Queue<DynamicBoneJY> m_loadingQueue = new Queue<DynamicBoneJY>();
        private Queue<DynamicBoneJY> m_removeQueue = new Queue<DynamicBoneJY>();

        // 每帧更新 维护DynamicBoneJY队列
        void UpdateQueue()
        {
            while(m_loadingQueue.Count > 0)
            {
                DynamicBoneJY target = m_loadingQueue.Dequeue();

                int idx = m_dynamicBoneList.IndexOf(target);
                if (idx == -1)
                {
                    m_dynamicBoneList.Add(target);



                    target.m_headInfo.m_jobDataOffset = m_particleInfo.Length;

                    int headIndex = m_headInfo.Length;
                    target.m_headInfo.ResetHeadIndex(headIndex);

                    m_headInfo.Add(target.m_headInfo);
                    m_particleInfo.AddRange(target.m_AllParticles);
                    m_headRootTransform.Add(target.m_rootParentTransform);

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

                    //是否是队列中末尾对象
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
                    }
                    else
                    {
                        //将最末列的HeadInfo 索引设置为当前将要移除的HeadInfo 索引
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

            var dataArrLength = m_DbDataLen * DynamicBoneJY.MAX_TRANSFORM_LIMIT;

            var rootJob = new RootPosApplyJob
            {
                ParticleHeadInfo = this.m_headInfo
            };
            var rootHandle = rootJob.Schedule(m_headRootTransform);

            var finalJob = new FinalJob
            {
                ParticleInfo = this.m_particleInfo,
            };
            var finalHandle = finalJob.Schedule(this.m_particleTransformArr, rootHandle);

            m_lastJobHandle = finalHandle;

            JobHandle.ScheduleBatchedJobs();
        }
        private void OnDestroy()
        {
            if (this.m_headRootTransform.isCreated)
            {
                this.m_headRootTransform.Dispose();
            }
        }
    }
}