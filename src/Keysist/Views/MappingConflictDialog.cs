using Keysist.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keysist.Views;

public enum MappingConflictChoice
{
    Cancel,
    Replace,
    UseAnyway,
}

public static class MappingConflictDialog
{
    public static async Task<MappingConflictChoice> ShowAsync(XamlRoot? xamlRoot, MappingConflict conflict)
    {
        var dialog = new ContentDialog
        {
            Title = "Trigger already used",
            Content = $"This trigger is already used by:\n\n{conflict.Summary}\n\nReplace that mapping, use this trigger anyway, or cancel?",
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Use anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => MappingConflictChoice.Replace,
            ContentDialogResult.Secondary => MappingConflictChoice.UseAnyway,
            _ => MappingConflictChoice.Cancel,
        };
    }
}
