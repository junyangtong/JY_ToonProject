using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

[BurstCompile]
struct BackgroundNoiseJob : IJob
{
    public float ElapsedTime;
    public float DeltaTime;
    [WriteOnly]
    public NativeArray<Vector3> Offsets;
    [ReadOnly]
    public NativeArray<Vector3> OriginPositions;
    public void Execute()
    {
        for (int i = 0; i < Offsets.Length; i++)
        {
            var p = OriginPositions[i];
            var sinx = Mathf.Sin(1f * ElapsedTime);
            var siny = Mathf.Cos(1f * ElapsedTime);
            var sinz = Mathf.Sin(1f * ElapsedTime);
            Offsets[i] = 2f * new Vector3(sinx, siny, sinz);
        }
    }
}

public class SingleJobDemo : MonoBehaviour
{
    public int WorldEdgeSize;
    private Transform[] m_cubes;
    private JobHandle m_jobHandle;
    private NativeArray<Vector3> m_nativeOffsets;
    //private Vector3[] m_originalPositions;
    private NativeArray<Vector3> m_nativePositions;

    void OnEnable()
    {
        m_cubes = new Transform[WorldEdgeSize * WorldEdgeSize * WorldEdgeSize];
        m_nativePositions = new NativeArray<Vector3>(m_cubes.Length, Allocator.Persistent);
        m_nativeOffsets = new NativeArray<Vector3>(m_cubes.Length, Allocator.Persistent);

        var index = 0;
        for (int x = 0; x < WorldEdgeSize; x++)
        {
            for (int y = 0; y < WorldEdgeSize; y++)
            {
                for (int z = 0; z < WorldEdgeSize; z++)
                {
                    m_cubes[index] = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                    m_cubes[index].position = new Vector3(x, y, z) * 5f - new Vector3(WorldEdgeSize * 5f * 0.5f, WorldEdgeSize * 5f * 0.5f, 0);
                    m_nativePositions[index] = m_cubes[index].position;
                    index++;
                }
            }
        }
    }

    void Update()
    {
        var noiseJob = new BackgroundNoiseJob
        {
            ElapsedTime = Time.time,
            DeltaTime = Time.deltaTime,
            Offsets = m_nativeOffsets,
            OriginPositions = m_nativePositions,
        };
        m_jobHandle = noiseJob.Schedule();
    }

    void LateUpdate()
    {
        m_jobHandle.Complete();
        for (int i = 0; i < m_nativeOffsets.Length; i++)
        {
            m_cubes[i].position = m_nativePositions[i] + m_nativeOffsets[i];
        }
    }

    void OnDisable()
    {
        m_nativeOffsets.Dispose();
        m_nativePositions.Dispose();
        for (int i = 0; i < m_cubes.Length; i++)
        {
            Destroy(m_cubes[i].gameObject);
        }
    }
}