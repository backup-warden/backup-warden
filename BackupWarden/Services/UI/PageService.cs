using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Views;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Services.UI
{
    /// <summary>
    /// Service that provides page type mappings for navigation
    /// </summary>
    public class PageService : IPageService
    {
        private readonly Dictionary<string, Type> _pages = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="PageService"/> class.
        /// </summary>
        public PageService()
        {
            // Configure the pages with their key mappings
            Configure<MainPage>("MainPage");
            
            // Add more page configurations here as they are added to the app
            // Configure<SettingsPage>("SettingsPage");
        }

        /// <summary>
        /// Gets the page type for the specified key
        /// </summary>
        /// <param name="key">The key representing the page</param>
        /// <returns>The type of the page</returns>
        /// <exception cref="ArgumentException">Thrown when the specified key is not found</exception>
        public Type GetPageType(string key)
        {
            Type? pageType;
            lock (_pages)
            {
                if (!_pages.TryGetValue(key, out pageType))
                {
                    throw new ArgumentException($"Page not found: {key}. Did you forget to configure the page?");
                }
            }

            return pageType;
        }

        /// <summary>
        /// Configures a page with its unique key
        /// </summary>
        /// <typeparam name="T">The type of the page</typeparam>
        /// <param name="key">The unique key for the page</param>
        private void Configure<T>(string key) where T : Page
        {
            lock (_pages)
            {
                if (_pages.ContainsKey(key))
                {
                    throw new ArgumentException($"The key {key} is already configured in PageService");
                }

                var type = typeof(T);
                if (_pages.Any(p => p.Value == type))
                {
                    throw new ArgumentException($"This type is already configured with key {_pages.First(p => p.Value == type).Key}");
                }

                _pages.Add(key, type);
            }
        }
    }
}
