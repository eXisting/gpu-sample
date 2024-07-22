using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GPU
{
  public class ParallelThreadCameraTextureReader : MonoBehaviour
  {
    [Header("[Camera]")]
    [SerializeField] private Camera currentCamera;
    [SerializeField] private RenderTexture portraitTexture;

    [Header("[Size settings]")]
    [SerializeField] private int suitableWidth = 720;
    [SerializeField] private int suitableHeight = 1280;

    [Header("[Texture Reader]")]
    [SerializeField] private RenderThreadGPURequest _gpuRequestAdapter;

    public event CameraTextureRead OnRead;

    public bool IsReading { get; private set; }

    private Task _readingTask = Task.CompletedTask;
    private CancellationTokenSource _tokenSource = new CancellationTokenSource();
    

    protected void OnDestroy()
    {
      StopReading();
    }

    protected void OnApplicationQuit()
    {
      StopReading();
    }

    public async void StartReading()
    {
      IsReading = true;

      if (_readingTask != null)
      {
        _tokenSource?.Cancel();
        await _readingTask;
      }

      if (!IsReading)
      {
        return;
      }

      _tokenSource = new CancellationTokenSource();
      _readingTask = ReadingTextureProcess(currentCamera.targetTexture, _tokenSource.Token);
    }

    public void StopReading()
    {
      IsReading = false;
      _tokenSource?.Cancel();
    }
    
    private async Task ReadingTextureProcess(Texture renderTexture, CancellationToken token)
    {
      var streamingTask = new TaskCompletionSource<bool>();
      token.Register(() => streamingTask.SetResult(true));

      var bufferPool = new List<byte[]>
      {
        new byte[1],
        new byte[1]
      };

      var bufferIndex = 0;

      var sendFrameTask = Task.CompletedTask;
      while (!streamingTask.Task.IsCompleted)
      {
        var gpuReadingTask = new TaskCompletionSource<RenderThreadGPURequest.CopyToBuffer>();
        _gpuRequestAdapter.RegisterRequest(renderTexture, gpuReadingTask.SetResult);

        await gpuReadingTask.Task;


        bufferIndex = (bufferIndex + 1) % 2;
        var currentBuffer = bufferPool[bufferIndex];

        var copyToManagedTask = Task.Run(gpuReadingTask.Task.Result(ref currentBuffer));


        await Task.WhenAny(streamingTask.Task, sendFrameTask);

        async Task ComboTask(int curIndex, Task<byte[]> copyToManagedLocalTask)
        {
          await Task.WhenAny(streamingTask.Task, copyToManagedLocalTask);

          if (streamingTask.Task.IsCompleted)
          {
            await Task.CompletedTask;
          }
          else if (copyToManagedLocalTask.Result == null)
          {
            await Task.CompletedTask;
          }
          else
          {
            bufferPool[curIndex] = copyToManagedLocalTask.Result;
            await Task.Run(GetReadbackCompleted(copyToManagedLocalTask.Result, renderTexture.width, renderTexture.height), token);
          }
        }

        sendFrameTask = ComboTask(bufferIndex, copyToManagedTask);
      }
    }

    private Action GetReadbackCompleted(byte[] buffer, int width, int height)
    {
      return () =>
      {
        OnRead?.Invoke(buffer, height, width);
      };
    }
  }
}