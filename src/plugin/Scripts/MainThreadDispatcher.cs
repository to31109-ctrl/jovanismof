using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MegabonkTogether.Common.Networking;
using UnityEngine;

namespace MegabonkTogether.Scripts
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly GameThreadContext context = new();
        public static bool IsMainThread => context.IsOwner;
        public void Awake() { context.DrainOne(); }

        public void Update()
        {
            for (int count = 0; count < 256; count++)
            {
                try
                {
                    if (!context.DrainOne()) break;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(ex);
                }
            }
        }

        public static void Enqueue(Action action)
        {
            context.Post(_ => action(), null);
        }
        public static Task Run(Func<Task> action) => context.RunAsync(action);
    }
}
