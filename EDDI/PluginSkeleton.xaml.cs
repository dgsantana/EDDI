using System.Windows;

namespace EDDI
{
    /// <summary>
    /// Interaction logic for PluginSkeleton.xaml
    /// </summary>
    public partial class PluginSkeleton
    {
        private readonly string _pluginName;

        public PluginSkeleton(string pluginName)
        {
            InitializeComponent();
            _pluginName = pluginName;
        }

        private void Pluginenabled_Checked(object sender, RoutedEventArgs e)
        {
            var configuration = EDDIConfiguration.FromFile();
            configuration.Plugins[_pluginName] = true;
            configuration.ToFile();
        }

        private void Pluginenabled_Unchecked(object sender, RoutedEventArgs e)
        {
            var configuration = EDDIConfiguration.FromFile();
            configuration.Plugins[_pluginName] = false;
            configuration.ToFile();
        }
    }
}
