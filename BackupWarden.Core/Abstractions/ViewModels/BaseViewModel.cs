using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Core.Abstractions.ViewModels
{
    public abstract class BaseViewModel<T> : ObservableObject where T : BaseViewModel<T>
    {
        public static string PageKey => typeof(T).FullName!;
    }
}
