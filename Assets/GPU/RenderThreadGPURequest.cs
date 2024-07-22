using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GPU
{
  public class RenderThreadGPURequest : MonoBehaviour
  {
    public delegate Func<byte[]> CopyToBuffer(ref byte[] bufferOut);

    private class RequestContext
    {
      public readonly Texture Source;

      public readonly Action<CopyToBuffer> CallBack;      
      
      public AsyncGPUReadbackPluginRequest GpuRequest;

      public RequestContext(Texture source, Action<CopyToBuffer> callBack)
      {
        Source = source;
        CallBack = callBack;
      }

      public Func<byte[]> CopyToBufferErrorToMany(ref byte[] buffer)
      {
        return () =>
        {
          return null;
        };
      }

      public Func<byte[]> CopyToBufferErrorGPU(ref byte[] buffer)
      {
        GpuRequest.Dispose();

        return () =>
        {
          return null;
        };
      }

      public Func<byte[]> CopyToBuffer(ref byte[] buffer)
      {
        var bufferInternal = buffer;

#if !UNITY_STANDALONE
        return () =>
        {
          var result = GpuRequest.GetRawData(bufferInternal);
          GpuRequest.Dispose();
          return result;
        };
#else
        return default;
#endif
      }
    }


    [SerializeField] private int maxRequestsAmount = 8;

    private readonly List<RequestContext> _newRequest = new List<RequestContext>();
    private readonly Queue<RequestContext> _activeRequests = new Queue<RequestContext>();
    private readonly Queue<NativeArray<byte>> _resultBufferPool = new Queue<NativeArray<byte>>();

    private Coroutine _updateRequestsCoroutine;


    protected void Update()
    {
      while (_activeRequests.Count > 0)
      {
        var req = _activeRequests.Peek();

        if (req.GpuRequest == null)
        {
          _activeRequests.Dequeue();
          req.CallBack(ReturnBufferToPoolWrapper(req, req.CopyToBufferErrorToMany));
          continue;
        }

        if (req.GpuRequest.hasError)
        {
          _activeRequests.Dequeue();
          req.CallBack(ReturnBufferToPoolWrapper(req, req.CopyToBufferErrorGPU));
        }
        else if (req.GpuRequest.done)
        {
          _activeRequests.Dequeue();
          req.CallBack(ReturnBufferToPoolWrapper(req, req.CopyToBuffer));
        }
        else
        {
          // You need to explicitly ask for an update regularly
          req.GpuRequest.Update();

          break;
        }
      }
    }

    public void RegisterRequest(Texture source, Action<CopyToBuffer> callback)
    {
      _newRequest.Add(new RequestContext(source, callback));

      if (_updateRequestsCoroutine == null)
      {
        _updateRequestsCoroutine = StartCoroutine(UpdateRequests());
      }
    }

    protected void OnDisable()
    {
      if(_updateRequestsCoroutine != null)
      {
        StopCoroutine(_updateRequestsCoroutine);
        _updateRequestsCoroutine = null;
      }      
    }

    private IEnumerator UpdateRequests()
    {
      while (true)
      {
        do
        {
          yield return new WaitForEndOfFrame();
        }
        while (_newRequest.Count == 0);

        foreach (var requestData in _newRequest)
        {
          if (_activeRequests.Count < maxRequestsAmount)
          {
            requestData.GpuRequest = AsyncGPUReadbackPlugin.Request(GetCorrectSuitable(requestData.Source), requestData.Source);
          }

          _activeRequests.Enqueue(requestData);
        }

        _newRequest.Clear();
      }
    }

    private NativeArray<byte> GetCorrectSuitable(Texture source)
    {
      var size = source.width * source.height * (int)GraphicsFormatUtility.GetBlockSize(source.graphicsFormat) * 8;

      var resultBuffer = GetBufferFromPool();
      if (resultBuffer.Length != size)
      {
        if (resultBuffer.IsCreated)
        {
          resultBuffer.Dispose();
        }
        resultBuffer = new NativeArray<byte>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      }

      return resultBuffer;
    }

    private NativeArray<byte> GetBufferFromPool()
    {
      if (_resultBufferPool.Count == 0)
      {
        _resultBufferPool.Enqueue(new NativeArray<byte>());
      }
      return _resultBufferPool.Dequeue();
    } 

    private CopyToBuffer ReturnBufferToPoolWrapper(RequestContext context, CopyToBuffer callBack)
    {
      return (ref byte[] bufferOut) =>
      {
        var result = callBack(ref bufferOut);
        if(context.GpuRequest != null)
        {
          _resultBufferPool.Enqueue(context.GpuRequest.ResultBuffer);
        }        

        return result;
      };      
    }

    public void ClearBuffersPool()
    {
      while (_resultBufferPool.Count != 0)
      {
        var buffer = _resultBufferPool.Dequeue();
        if (buffer.IsCreated)
        {
          buffer.Dispose();
        }
      }
    }
  }

  
}