using EDDI;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiStarMapService;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EddiEdsmResponder
{
    /// <summary>
    /// Interaction logic for ConfigurationWindow.xaml
    /// </summary>
    public partial class ConfigurationWindow : UserControl
    {
        public ConfigurationWindow()
        {
            InitializeComponent();

            StarMapConfiguration starMapConfiguration = StarMapConfiguration.FromFile();
            edsmApiKeyTextBox.Text = starMapConfiguration.ApiKey;
            edsmCommanderNameTextBox.Text = starMapConfiguration.CommanderName;
            edsmFetchLogsButton.Content = String.IsNullOrEmpty(edsmApiKeyTextBox.Text) ? "Please enter EDSM API key  to obtain log" : "Obtain log";
        }

        private void EdsmCommanderNameChanged(object sender, TextChangedEventArgs e)
        {
            edsmFetchLogsButton.IsEnabled = true;
            edsmFetchLogsButton.Content = "Obtain log";
            UpdateEdsmConfiguration();
        }

        private void EdsmApiKeyChanged(object sender, TextChangedEventArgs e)
        {
            edsmFetchLogsButton.IsEnabled = true;
            edsmFetchLogsButton.Content = String.IsNullOrEmpty(edsmApiKeyTextBox.Text) ? "Please enter EDSM API key  to obtain log" : "Obtain log";
            UpdateEdsmConfiguration();
        }

        private void UpdateEdsmConfiguration()
        {
            StarMapConfiguration edsmConfiguration = StarMapConfiguration.FromFile();
            if (!string.IsNullOrWhiteSpace(edsmApiKeyTextBox.Text))
            {
                edsmConfiguration.ApiKey = edsmApiKeyTextBox.Text.Trim();
            }
            if (!string.IsNullOrWhiteSpace(edsmCommanderNameTextBox.Text))
            {
                edsmConfiguration.CommanderName = edsmCommanderNameTextBox.Text.Trim();
            }
            edsmConfiguration.ToFile();
            EDDI.Core.Eddi.Instance.Reload("EDSM responder");
        }

        /// <summary>
        /// Obtain the EDSM log and sync it with the local datastore
        /// </summary>
        private async void EdsmObtainLogClicked(object sender, RoutedEventArgs e)
        {
            StarMapConfiguration starMapConfiguration = StarMapConfiguration.FromFile();

            if (string.IsNullOrEmpty(starMapConfiguration.ApiKey))
            {
                edsmFetchLogsButton.IsEnabled = false;
                edsmFetchLogsButton.Content = "Please enter EDSM API key  to obtain log";
                return;
            }

            string commanderName;
            if (string.IsNullOrEmpty(starMapConfiguration.CommanderName))
            {
                // Fetch the commander name from the companion app
                Commander cmdr = EDDI.Core.Eddi.Instance.Cmdr;
                if (cmdr?.name != null)
                {
                    commanderName = cmdr.name;
                }
                else
                {
                    edsmFetchLogsButton.IsEnabled = false;
                    edsmFetchLogsButton.Content = "Companion app not configured and no name supplied; cannot obtain logs";
                    return;
                }
            }
            else
            {
                commanderName = starMapConfiguration.CommanderName;
            }

            edsmFetchLogsButton.IsEnabled = false;
            edsmFetchLogsButton.Content = "Obtaining log...";

            var progress = new Progress<string>(s => edsmFetchLogsButton.Content = s);
            await Task.Factory.StartNew(() => ObtainEdsmLogs(starMapConfiguration, commanderName, progress),
                                            TaskCreationOptions.LongRunning);

            starMapConfiguration.LastSync = DateTime.UtcNow;
            starMapConfiguration.ToFile();
        }

        public static void ObtainEdsmLogs(StarMapConfiguration starMapConfiguration, string commanderName, IProgress<string> progress)
        {
            StarMapService starMapService = new StarMapService(starMapConfiguration.ApiKey, commanderName);
            try
            {
                Dictionary<string, StarMapLogInfo> systems = starMapService.GetStarMapLog();
                Dictionary<string, string> comments = starMapService.GetStarMapComments();
                int total = systems.Count;
                int i = 0;
                foreach (string system in systems.Keys)
                {
                    progress.Report("Obtaining log " + i++ + "/" + total);
                    StarSystem currentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(system, false);
                    currentStarSystem.visits = systems[system].Visits;
                    currentStarSystem.lastvisit = systems[system].LastVisit;
                    if (comments.ContainsKey(system))
                    {
                        currentStarSystem.comment = comments[system];
                    }
                    StarSystemSqLiteRepository.Instance.SaveStarSystem(currentStarSystem);
                }
                progress.Report("Obtained log");
            }
            catch (EDSMException edsme)
            {
                progress.Report("EDSM error received: " + edsme.Message);
            }
        }
    }
}
