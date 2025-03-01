﻿using System;
using System.Collections.Generic;
using AppTrackingTransparency.Common;

namespace AppTrackingTransparency.Native
{
    public class NativeAppTrackingTransparencyManager : IAppTrackingTransparencyManager
    {
#if UNITY_IOS
        public string Idfa => UnityEngine.iOS.Device.advertisingIdentifier;
#else
        public string Idfa => "00000000-0000-0000-0000-000000000000";
#endif

        public AppTrackingTransparencyAuthorizationStatus TrackingAuthorizationStatus
        {
            get
            {
#if UNITY_IOS
                var rawAuthorizationStatus = PInvoke.AppTrackingTransparencyManager_GetTrackingAuthorizationStatus();
#else
                var rawAuthorizationStatus = 0U;
#endif
                return GetAuthorizationStatusFromUnsignedInteger(rawAuthorizationStatus);
            }
        }

        public void RequestTrackingAuthorization(Action<AppTrackingTransparencyAuthorizationStatus> completion)
        {
            var requestId = CallbackHandler.AddRequestTrackingAuthorizationCallback(rawAuthorizationStatus =>
            {
                completion(GetAuthorizationStatusFromUnsignedInteger(rawAuthorizationStatus));
            });

#if UNITY_IOS
            PInvoke.AppTrackingTransparencyManager_RequestTrackingAuthorizationCallback(requestId);
#endif
        }

        public void Update()
        {
            CallbackHandler.ExecutePendingCallbacks();
        }

        private static AppTrackingTransparencyAuthorizationStatus GetAuthorizationStatusFromUnsignedInteger(uint rawAuthorizationStatus)
        {
            return (AppTrackingTransparencyAuthorizationStatus) rawAuthorizationStatus;
        }

        private static class CallbackHandler
        {
            private const uint InitialCallbackId = 1U;
            private const uint MaxCallbackId = uint.MaxValue;

            private static readonly object SyncLock = new object();
            private static readonly Dictionary<uint, Action<uint>> RequestTrackingAuthorizationCallbackDictionary = new Dictionary<uint, Action<uint>>();
            private static readonly List<Action> ScheduledActions = new List<Action>();

            private static bool _isInitialized;
            private static uint _callbackId = InitialCallbackId;

            public static uint AddRequestTrackingAuthorizationCallback(Action<uint> callback)
            {
                if (callback == null)
                {
                    throw new Exception("Can't add a null callback.");
                }

                if (!_isInitialized)
                {
#if UNITY_IOS
                    PInvoke.AppTrackingTransparencyManager_SetRequestTrackingAuthorizationCallbackHandler(
                        PInvoke.RequestTrackingAuthorizationCallbackHandler);
#endif

                    _isInitialized = true;
                }

                uint usedCallbackId;
                lock (SyncLock)
                {
                    usedCallbackId = ++_callbackId;
                    if (_callbackId >= MaxCallbackId)
                        _callbackId = InitialCallbackId;

                    RequestTrackingAuthorizationCallbackDictionary.Add(usedCallbackId, callback);
                }
                return usedCallbackId;
            }

            public static void ScheduleRequestTrackingAuthorizationCallback(uint requestId, uint rawAuthorizationStatus)
            {
                lock (SyncLock)
                {
                    Action<uint> callback;
                    if (RequestTrackingAuthorizationCallbackDictionary.TryGetValue(requestId, out callback))
                    {
                        ScheduledActions.Add(() => callback.Invoke(rawAuthorizationStatus));
                        RequestTrackingAuthorizationCallbackDictionary.Remove(requestId);
                    }
                }
            }

            public static void ExecutePendingCallbacks()
            {
                lock (SyncLock)
                {
                    while (ScheduledActions.Count > 0)
                    {
                        var action = ScheduledActions[0];
                        ScheduledActions.RemoveAt(0);
                        action.Invoke();
                    }
                }
            }
        }

        private static class PInvoke
        {
            private const string DllName = "__Internal";
            public delegate void RequestTrackingAuthorizationCallbackDelegate(uint requestId, uint rawAuthorizationStatus);

            [AOT.MonoPInvokeCallback(typeof(RequestTrackingAuthorizationCallbackDelegate))]
            public static void RequestTrackingAuthorizationCallbackHandler(uint requestId, uint rawAuthorizationStatus)
            {
                try
                {
                    CallbackHandler.ScheduleRequestTrackingAuthorizationCallback(requestId, rawAuthorizationStatus);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("Received exception while scheduling a callback for request ID " + requestId);
                    Console.WriteLine("Received authorization status " + rawAuthorizationStatus);
                    Console.WriteLine("Exception: " + exception);
                }
            }

#if UNITY_IOS

            [System.Runtime.InteropServices.DllImport(DllName)]
            public static extern uint AppTrackingTransparencyManager_GetTrackingAuthorizationStatus();

            [System.Runtime.InteropServices.DllImport(DllName)]
            public static extern void AppTrackingTransparencyManager_SetRequestTrackingAuthorizationCallbackHandler(RequestTrackingAuthorizationCallbackDelegate callbackHandler);

            [System.Runtime.InteropServices.DllImport(DllName)]
            public static extern void AppTrackingTransparencyManager_RequestTrackingAuthorizationCallback(uint requestId);
#endif
        }
    }
}
