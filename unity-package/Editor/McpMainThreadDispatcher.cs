using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityMcpBridge.Editor
{
    [InitializeOnLoad]
    public static class McpMainThreadDispatcher
    {
        private struct WorkItem
        {
            public Func<string> Work;
            public TaskCompletionSource<string> Tcs;
        }

        private static readonly ConcurrentQueue<WorkItem> Queue = new();

        static McpMainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        private static void ProcessQueue()
        {
            while (Queue.TryDequeue(out var item))
            {
                try
                {
                    var result = item.Work();
                    item.Tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    item.Tcs.SetException(ex);
                }
            }
        }

        public static Task<string> RunOnMainThread(Func<string> work)
        {
            var tcs = new TaskCompletionSource<string>();
            Queue.Enqueue(new WorkItem { Work = work, Tcs = tcs });
            return tcs.Task;
        }
    }
}
