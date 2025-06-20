using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Core.Abstractions.ViewModels;
using BackupWarden.Core.ViewModels;
using BackupWarden.Views;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BackupWarden.Services.UI
{
    /// <summary>
    /// Service that provides page type mappings for navigation
    /// </summary>
    public class PageService : IPageService
    {
        private readonly Dictionary<string, Type> _pages = [];

        public PageService()
        {
            Configure<MainViewModel, MainPage>();
        }

        public Type GetPageType(string key)
        {
            Type? pageType;
            lock (_pages)
            {
                if (!_pages.TryGetValue(key, out pageType))
                {
                    throw new ArgumentException($"Page not found: {key}. Did you forget to call PageService.Configure?");
                }
            }

            return pageType;
        }

        private void Configure<VM, V>()
            where VM : BaseViewModel<VM>
            where V : Page
        {
            lock (_pages)
            {
                var key = BaseViewModel<VM>.PageKey;
                if (_pages.ContainsKey(key))
                {
                    throw new ArgumentException($"The key {key} is already configured in PageService");
                }

                var type = typeof(V);
                if (_pages.ContainsValue(type))
                {
                    throw new ArgumentException($"This type is already configured with key {_pages.First(p => p.Value == type).Key}");
                }

                _pages.Add(key, type);
            }
        }
    }
}
