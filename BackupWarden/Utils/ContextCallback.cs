using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackupWarden.Utils
{
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
            {
                _syncContext.Post(_ => _callback(arg1, arg2), null);
            }
            else
            {
                _callback(arg1, arg2);
            }
        }
    }
}
