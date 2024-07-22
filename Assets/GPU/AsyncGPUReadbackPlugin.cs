using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPU
{

  // Tries to match the official API
  public class AsyncGPUReadbackPlugin
  {
    

    public static AsyncGPUReadbackPluginRequest Request(NativeArray<byte> resultBuffer, Texture src)
    {
#if !UNITY_STANDALONE
      return new AsyncGPUReadbackPluginRequest(resultBuffer, src);
#else
      return new AsyncGPUReadbackPluginRequest();
#endif
    }
  }

  public class AsyncGPUReadbackPluginRequest
  {
    public delegate void RequestUpdater(Func<bool, bool> work);

    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern bool isCompatible();
    [DllImport("AsyncGPUReadbackPlugin")]
#if !UNITY_STANDALONE
    private static extern unsafe int makeRequest_mainThread(int texture, int miplevel, void* resutBuffer, int length);
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern unsafe void getData_mainThread(int event_id, ref void* buffer, ref int length);
    [DllImport("AsyncGPUReadbackPlugin")]
#endif
    private static extern IntPtr getfunction_makeRequest_renderThread();
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern void makeRequest_renderThread(int event_id);
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern IntPtr getfunction_update_renderThread();
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern bool isRequestError(int event_id);
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern bool isRequestDone(int event_id);
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern void dispose(int event_id);
    [DllImport("AsyncGPUReadbackPlugin")]
    private static extern void __DLL__AddDebugLogMethod(_callback_string_int_delegate callback);

    /// <summary>
    /// Tell if we are using the plugin api or the official api
    /// </summary>
    private bool usePlugin;
    /// <summary>
    /// Event Id used to tell what texture is targeted to the render thread
    /// </summary>
    private int eventId;
    /// <summary>
    /// Official api request object used if supported
    /// </summary>
    private AsyncGPUReadbackRequest gpuRequest;

    private NativeArray<byte> _resultBuffer;
    public NativeArray<byte> ResultBuffer => _resultBuffer;


        /// <summary>
    /// Check if the request is done
    /// </summary>
    public bool done
    {
      get
      {
        if (usePlugin)
        {
          return isRequestDone(eventId);
        }
        else
        {
          return gpuRequest.done;
        }
      }
    }

    /// <summary>
    /// Check if the request has an error
    /// </summary>
    public bool hasError
    {
      get
      {
        if (usePlugin)
        {
          return isRequestError(eventId);
        }
        else
        {
          return gpuRequest.hasError;
        }
      }
    }

    private static Dictionary<Texture, IntPtr> _cacheTexturePtr = new Dictionary<Texture, IntPtr>();

#if !UNITY_STANDALONE
    /// <summary>
    /// Create an AsyncGPUReadbackPluginRequest.
    /// Use official AsyncGPUReadback.Request if possible.
    /// If not, it tries to use OpenGL specific implementation
    /// Warning! Can only be called from render thread yet (not main thread)
    /// </summary>
    /// <param name="src"></param>
    /// <returns></returns>
    public unsafe AsyncGPUReadbackPluginRequest(NativeArray<byte> resultBuffer, Texture src)
    {
      _resultBuffer = resultBuffer;

      if (SystemInfo.supportsAsyncGPUReadback)
      {
        usePlugin = false;
        gpuRequest = AsyncGPUReadback.RequestIntoNativeArray(ref _resultBuffer, src);
      }
      else if (isCompatible())
      {
        // Set C++ console
        __DLL__AddDebugLogMethod(_CPP_DebugLog);

        usePlugin = true;

        if (!_cacheTexturePtr.TryGetValue(src, out IntPtr cachedPtr))
        {
          cachedPtr = src.GetNativeTexturePtr();
          _cacheTexturePtr.Add(src, cachedPtr);
        }

        var textureId = (int)cachedPtr;

        eventId = makeRequest_mainThread(textureId, 0, NativeArrayUnsafeUtility.GetUnsafePtr(_resultBuffer), _resultBuffer.Length);
        GL.IssuePluginEvent(getfunction_makeRequest_renderThread(), eventId);
      }
    }

    public unsafe byte[] GetRawData(byte[] buffer)
    {
      if (usePlugin)
      {
        // Get data from cpp plugin
        void* ptr = null;
        var length = 0;
        getData_mainThread(eventId, ref ptr, ref length);

        if(length != _resultBuffer.Length)
        {
         // Debug.LogError($"GetRawData: _resultBuffer.Dispose()! length:{_resultBuffer.Length} realLength:{length}");
          if (_resultBuffer.IsCreated)
          {
            _resultBuffer.Dispose();
          }          

          _resultBuffer = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(ptr, length, Allocator.Persistent);
        }          

      }

      if (buffer.Length != _resultBuffer.Length)
      {
        buffer = new byte[_resultBuffer.Length];
      }

      _resultBuffer.CopyTo(buffer);

      return buffer;
    }
#endif
    
    /// <summary>
    /// Has to be called regularly to update request status.
    /// Call this from Update() or from a corountine
    /// </summary>
    /// <param name="force">Update is automatic on official api,
    /// so we don't call the Update() method except on force mode.</param>
    public void Update(bool force = false)
    {
      if (usePlugin)
      {
        GL.IssuePluginEvent(getfunction_update_renderThread(), eventId);
      }
      else if (force)
      {
        gpuRequest.Update();
      }
    }

    /// <summary>
    /// Has to be called to free the allocated buffer after it has been used
    /// </summary>
    public void Dispose()
    {
      if (usePlugin)
      {
        dispose(eventId);
      }
    }

    //
    // C++から呼び出すDebug.Logのデリゲート
    delegate void _callback_string_int_delegate(string key, int val);
    [AOT.MonoPInvokeCallbackAttribute(typeof(_callback_string_int_delegate))]
    private static void _CPP_DebugLog(string key, int val)
    {
      Debug.Log("<color=cyan>[FromCPP]:" + key + ":" + val + "</color>");
    }
  }
}