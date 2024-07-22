using System;
using UnityEngine;

namespace GPU
{
    public class Testing : MonoBehaviour
    {
        [SerializeField] private ParallelThreadCameraTextureReader reader;
        
        public void Begin()
        {
            reader.OnRead += BytesRead;
        }

        public void Stop()
        {
            reader.OnRead -= BytesRead;
        }
        
        void BytesRead(byte[] buffer, int height, int width)
        {
            Debug.Log($"Bytes are captured: {buffer.Length} {height} {width}");
        }
    }
}