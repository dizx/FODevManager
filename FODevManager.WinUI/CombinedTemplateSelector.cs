using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FODevManager.WinUI.ViewModel;

namespace FODevManager.WinUI
{
    public sealed class CombinedTemplateSelector : DataTemplateSelector
    {
        public DataTemplate RepoTemplate { get; set; }
        public DataTemplate NonGitTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return item switch
            {
                RepoGroupViewModel => RepoTemplate,
                ProfileEnvironmentViewModel => NonGitTemplate,
                _ => NonGitTemplate
            };
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
            => SelectTemplateCore(item);
    }
}
