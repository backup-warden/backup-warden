using System;

namespace BackupWarden.Abstractions.Services.UI
{
    /// <summary>
    /// Interface for a service that provides page type mappings for navigation
    /// </summary>
    public interface IPageService
    {
        /// <summary>
        /// Gets the page type based on a page key
        /// </summary>
        /// <param name="key">The unique identifier for the page</param>
        /// <returns>The type of the page</returns>
        Type GetPageType(string key);
    }
}
