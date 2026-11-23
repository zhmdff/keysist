using Keysist.Diagnostics;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Keysist.Services;

public static class FileBrowseHelper
{
    public static async Task<string?> PickLaunchTargetAsync(XamlRoot? xamlRoot)
    {
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                Logger.Log("FileBrowseHelper: no window handle");
                return null;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".cmd");
            picker.FileTypeFilter.Add(".ps1");
            picker.FileTypeFilter.Add(".vbs");
            picker.FileTypeFilter.Add(".msi");
            picker.FileTypeFilter.Add(".com");
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            Logger.Log($"FileBrowseHelper failed: {ex}");
            return null;
        }
    }

    public static async Task<string?> PickFolderAsync()
    {
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero)
                return null;

            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            Logger.Log($"FileBrowseHelper.PickFolder failed: {ex}");
            return null;
        }
    }

    private static IntPtr GetWindowHandle()
    {
        var window = MainWindow.Instance;
        if (window is null)
            return IntPtr.Zero;
        return WindowNative.GetWindowHandle(window);
    }
}
