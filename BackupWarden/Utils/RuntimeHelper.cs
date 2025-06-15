using Windows.Win32;
using Windows.Win32.Foundation;

namespace BackupWarden.Utils
{
    public class RuntimeHelper
    {
        public static unsafe bool IsMSIX
        {
            get
            {
                uint length = 0;
                // Check if the function call indicates that the process is not packaged (returns ERROR_NOT_FOUND or APPMODEL_ERROR_NO_PACKAGE).
                // A result of 0 (ERROR_SUCCESS) or ERROR_INSUFFICIENT_BUFFER (122) means it is packaged.
                // APPMODEL_ERROR_NO_PACKAGE (15700) specifically means the process has no package identity.
                WIN32_ERROR result = PInvoke.GetCurrentPackageFullName(ref length, []);
                return result != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
            }
        }
    }
}
