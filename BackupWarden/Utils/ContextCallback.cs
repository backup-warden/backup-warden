using System;
using System.Threading;

namespace BackupWarden.Utils
{
    public class ContextCallback<T1>
    {
        private readonly Action<T1> _callback;
        private readonly SynchronizationContext? _syncContext;

        public ContextCallback(Action<T1> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _syncContext = SynchronizationContext.Current;
        }

        public void Invoke(T1 arg1)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => _callback(arg1), null);
            else
                _callback(arg1);
        }
    }

    public class ContextCallback<T1, T2>
    {
        private readonly Action<T1, T2> _callback;
        private readonly SynchronizationContext? _syncContext;

        public ContextCallback(Action<T1, T2> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _syncContext = SynchronizationContext.Current;
        }

        public void Invoke(T1 arg1, T2 arg2)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => _callback(arg1, arg2), null);
            else
                _callback(arg1, arg2);
        }
    }

    public class ContextCallback<T1, T2, T3>
    {
        private readonly Action<T1, T2, T3> _callback;
        private readonly SynchronizationContext? _syncContext;

        public ContextCallback(Action<T1, T2, T3> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _syncContext = SynchronizationContext.Current;
        }

        public void Invoke(T1 arg1, T2 arg2, T3 arg3)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => _callback(arg1, arg2, arg3), null);
            else
                _callback(arg1, arg2, arg3);
        }
    }

    public class ContextCallback<T1, T2, T3, T4>
    {
        private readonly Action<T1, T2, T3, T4> _callback;
        private readonly SynchronizationContext? _syncContext;

        public ContextCallback(Action<T1, T2, T3, T4> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _syncContext = SynchronizationContext.Current;
        }

        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => _callback(arg1, arg2, arg3, arg4), null);
            else
                _callback(arg1, arg2, arg3, arg4);
        }
    }

    public class ContextCallback<T1, T2, T3, T4, T5>
    {
        private readonly Action<T1, T2, T3, T4, T5> _callback;
        private readonly SynchronizationContext? _syncContext;

        public ContextCallback(Action<T1, T2, T3, T4, T5> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _syncContext = SynchronizationContext.Current;
        }

        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => _callback(arg1, arg2, arg3, arg4, arg5), null);
            else
                _callback(arg1, arg2, arg3, arg4, arg5);
        }
    }
}