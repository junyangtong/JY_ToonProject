using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JY.Toon.Bartending
{
    [RequireComponent(typeof(Rigidbody))]
    public class FloatingObjects : MonoBehaviour
    {
        [SerializeField] private float buoyancyForceStrength = 200f; 
        [SerializeField] private float maxBuoyancyForce = 100f; 
        [SerializeField] private float centerToBottomOffset = 0f;
        [SerializeField] private Vector3 drag = new(1f, 1f, 1f);
        [SerializeField] private float angularDrag = 1f;
        [SerializeField] private float buoyancyTorqueStrength = 1f;

        private Rigidbody rigidBody;
        private float liquidHeight;
        private Vector3 liquidObjectPosWS;
        private float liquidHeight01;
        private float maxLiquidHeight;
        private float waveAmplitude;
        private float waveFrequency;
        private float waveSpeed;
        private Renderer liquidRenderer;
        
        private void Start()
        {
            rigidBody = transform.GetComponent<Rigidbody>();
            maxLiquidHeight = BartendingManager.Instance.MaxLiquidHeight;
            waveFrequency = BartendingManager.Instance.WaveFrequency;
            waveSpeed = BartendingManager.Instance.WaveSpeed;
            waveAmplitude = BartendingManager.Instance.WaveAmplitude;
            liquidRenderer = BartendingManager.Instance.LiquidRenderer;

            liquidObjectPosWS = liquidRenderer.transform.position;
        }

        // 计算波浪 和shader中一致
        struct WaveInfo
        {
            public float height;
            public Vector3 normal;
        };
        WaveInfo CalculateWave (Vector3 position)
        {
            WaveInfo waveInfo;

            float time = Time.time * waveSpeed;

            float waveHeight = waveAmplitude * 0.05f * Mathf.Sin(position.x * waveFrequency + time) 
                             * waveAmplitude * 0.05f * Mathf.Sin(position.z * waveFrequency + time);
            waveInfo.height = waveHeight;

            Vector3 T = new Vector3
            (
                1f,
                waveAmplitude * 0.05f * waveFrequency * Mathf.Cos(position.x * waveFrequency + time) 
                * waveAmplitude * 0.05f * Mathf.Sin(position.z * waveFrequency + time),
                0f
            );
            Vector3 B = new Vector3
            (
                0f,
                waveAmplitude * 0.05f * waveFrequency * Mathf.Sin(position.x * waveFrequency + time) 
                * waveAmplitude * 0.05f * Mathf.Cos(position.z * waveFrequency + time),
                1f
            );

            Vector3 N = Vector3.Cross(B, T);
            Vector3 normal = N.normalized;
            
            waveInfo.normal = normal;
            return waveInfo;
        }

        private void FixedUpdate()
        {
            // 漂浮物的相对高度
            Vector3 relativePosition = transform.position - liquidObjectPosWS;

            // 计算波浪
            waveAmplitude = BartendingManager.Instance.WaveAmplitude;
            WaveInfo waveInfo = CalculateWave(relativePosition);

            // 当前液面相对高度
            liquidHeight01 = BartendingManager.Instance.LiquidHeight01;
            liquidHeight = liquidHeight01 * maxLiquidHeight + waveInfo.height;

            // F浮=液体的密度×体积×重力加速度
            float bottomDepth = liquidHeight - relativePosition.y + centerToBottomOffset;
            Vector3 buoyancy = buoyancyForceStrength * bottomDepth * bottomDepth * bottomDepth * -Physics.gravity.normalized;
            buoyancy = Vector3.ClampMagnitude(buoyancy, maxBuoyancyForce);
            rigidBody.AddForce(buoyancy, ForceMode.Acceleration);

            // 旋转
            Vector3 normalLatitudinal = waveInfo.normal;
            Vector3 torque = Vector3.Cross(transform.up, normalLatitudinal);
            rigidBody.AddTorque(torque * buoyancyTorqueStrength, ForceMode.Acceleration);
            //Debug.Log($"bottomDepth{bottomDepth} + liquidHeight{liquidHeight} + localPosition{relativePosition.y} + centerToBottomOffset{centerToBottomOffset}");
            
            // 添加阻力
            rigidBody.AddTorque(-angularDrag * rigidBody.angularVelocity);
            var forcePosition = rigidBody.worldCenterOfMass + 1f * Vector3.up;
            rigidBody.AddForceAtPosition(drag.x * Vector3.Dot(transform.right, -rigidBody.velocity) * transform.right, forcePosition, ForceMode.Acceleration);
            rigidBody.AddForceAtPosition(drag.y * Vector3.Dot(Vector3.up, -rigidBody.velocity) * Vector3.up, forcePosition, ForceMode.Acceleration);
            rigidBody.AddForceAtPosition(drag.z * Vector3.Dot(transform.forward, -rigidBody.velocity) * transform.forward, forcePosition, ForceMode.Acceleration);
            
            // Debug
            Debug.DrawRay(transform.position, waveInfo.normal * 0.5f, Color.blue);
            Debug.DrawRay(transform.position, torque, Color.red);
        }
    }
}