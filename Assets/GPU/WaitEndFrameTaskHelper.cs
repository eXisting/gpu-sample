using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace GPU
{
  public class WaitEndFrameTaskHelper : MonoBehaviour
  {
    private Coroutine _coroutine;
    private TaskCompletionSource<bool> _endFrameAwaiter = null;

    private bool _playOnce;
    public async Task Awaiter(bool playOnce)
    {
      _playOnce = playOnce;
      if (_coroutine == null)
      {
        _coroutine = StartCoroutine(WaitEndFrameProcess(new WaitForEndOfFrame()));
      }

      if (_endFrameAwaiter == null)
      {
        _endFrameAwaiter = new TaskCompletionSource<bool>();
      }

      await _endFrameAwaiter.Task;
    }

    private IEnumerator WaitEndFrameProcess(WaitForEndOfFrame waitForEndOfFrame)
    {
      do
      {
        yield return waitForEndOfFrame;

        var temp = _endFrameAwaiter;
        _endFrameAwaiter = null;

        temp?.TrySetResult(true);
      }
      while (!_playOnce);

      Destroy(this);
    }

    private void OnDestroy()
    {
      _endFrameAwaiter?.TrySetResult(false);
    }

    private void OnDisable()
    {
      _endFrameAwaiter?.TrySetResult(false);
    }
  }
}