using System.Collections.Generic;
using System.Threading.Tasks;
namespace BackupWarden.Core.Abstractions.Services.UI
{
    public interface IPickerService
    {
        Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> fileTypeFilters, bool allowMultiple = false);
        Task<string?> PickFolderAsync();
    }
}
