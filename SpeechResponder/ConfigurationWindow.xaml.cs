using EDDI;
using EddiEvents;
using EddiJournalMonitor;
using EddiShipMonitor;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Utilities;

namespace EddiSpeechResponder
{
    /// <summary>
    /// Interaction logic for ConfigurationWindow.xaml
    /// </summary>
    public partial class ConfigurationWindow : INotifyPropertyChanged
    {
        private ObservableCollection<Personality> _personalities;
        public ObservableCollection<Personality> Personalities
        {
            get => _personalities;
            set { _personalities = value; OnPropertyChanged("Personalities"); }
        }
        private Personality _personality;
        public Personality Personality
        {
            get => _personality;
            set
            {
                _personality = value;
                ViewEditContent = value != null && value.IsEditable ? "Edit" : "View";
                OnPropertyChanged("Personality");
            }
        }
        public string ViewEditContent = "View";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ConfigurationWindow()
        {
            InitializeComponent();
            DataContext = this;

            var personalities =
                new ObservableCollection<Personality> {Personality.Default()};
            // Add our default personality
            foreach (Personality personality in Personality.AllFromDirectory())
            {
                personalities.Add(personality);
            }
            // Add local personalities
            foreach (Personality personality in personalities)
            {
                Logging.Debug("Found personality " + personality.Name);
            }
            Personalities = personalities;

            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            subtitlesCheckbox.IsChecked = configuration.Subtitles;
            subtitlesOnlyCheckbox.IsChecked = configuration.SubtitlesOnly;

            foreach (Personality personality in Personalities)
            {
                if (personality.Name == configuration.Personality)
                {
                    Personality = personality;
                    PersonalityDefaultTxt(personality);
                    break;
                }
            }
        }

        private void EddiScriptsUpdated(object sender, RoutedEventArgs e)
        {
            UpdateScriptsConfiguration();
        }

        private void EddiScriptsUpdated(object sender, DataTransferEventArgs e)
        {
            UpdateScriptsConfiguration();
        }

        private void EditScript(object sender, RoutedEventArgs e)
        {
            Script script = ((KeyValuePair<string, Script>)((Button)e.Source).DataContext).Value;
            EditScriptWindow editScriptWindow = new EditScriptWindow(Personality.Scripts, script.Name);
            editScriptWindow.ShowDialog();
            scriptsData.Items.Refresh();
        }

        private void ViewScript(object sender, RoutedEventArgs e)
        {
            Script script = ((KeyValuePair<string, Script>)((Button)e.Source).DataContext).Value;
            ViewScriptWindow viewScriptWindow = new ViewScriptWindow(Personality.Scripts, script.Name);
            viewScriptWindow.Show();
        }

        private void TestScript(object sender, RoutedEventArgs e)
        {
            Script script = ((KeyValuePair<string, Script>)((Button)e.Source).DataContext).Value;
            SpeechResponder responder = new SpeechResponder();
            responder.Start();
            // See if we have a sample
            List<Event> sampleEvents;
            var sample = Events.SampleByName(script.Name);
            switch (sample)
            {
                case null:
                    sampleEvents = new List<Event>();
                    break;
                case string _:
                    // It's as tring so a journal entry.  Parse it
                    sampleEvents = JournalMonitor.ParseJournalEntry((string)sample);
                    break;
                case Event _:
                    // It's a direct event
                    sampleEvents = new List<Event>() { (Event)sample };
                    break;
                default:
                    Logging.Warn("Unknown sample type " + sample.GetType());
                    sampleEvents = new List<Event>();
                    break;
            }

            ScriptResolver scriptResolver = new ScriptResolver(Personality.Scripts);
            if (sampleEvents.Count == 0)
            {
                sampleEvents.Add(null);
            }
            foreach (Event sampleEvent in sampleEvents)
            {
                responder.Say(scriptResolver, ((ShipMonitor)EDDI.Core.Eddi.Instance.ObtainMonitor("Ship monitor")).GetCurrentShip(), script.Name, sampleEvent, null, null, false);
            }
        }

        private void DeleteScript(object sender, RoutedEventArgs e)
        {
            Script script = ((KeyValuePair<string, Script>)((Button)e.Source).DataContext).Value;
            string messageBoxText = "Are you sure you want to delete the \"" + script.Name + "\" script?";
            string caption = "Delete Script";
            MessageBoxResult result = MessageBox.Show(messageBoxText, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Remove the script from the list
                    Personality.Scripts.Remove(script.Name);
                    Personality.ToFile();
                    EDDI.Core.Eddi.Instance.Reload("Speech responder");
                    // We updated a property of the personality but not the personality itself so need to manually update items
                    scriptsData.Items.Refresh();
                    break;
            }
        }

        private void ResetScript(object sender, RoutedEventArgs e)
        {
            Script script = ((KeyValuePair<string, Script>)((Button)e.Source).DataContext).Value;
            script.Value = null;
            EddiScriptsUpdated(sender, e);
            scriptsData.Items.Refresh();
        }

        private void UpdateScriptsConfiguration()
        {
            if (Personality != null)
            {
                Personality.ToFile();
                EDDI.Core.Eddi.Instance.Reload("Speech responder");
            }
        }

        private void PersonalityChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Personality != null)
            {
                PersonalityDefaultTxt(Personality);
                SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
                configuration.Personality = Personality.Name;
                configuration.ToFile();
                EDDI.Core.Eddi.Instance.Reload("Speech responder");
            }
        }

        private void NewScriptClicked(object sender, RoutedEventArgs e)
        {
            string baseName = "New function";
            string scriptName = baseName;
            int i = 2;
            while (Personality.Scripts.ContainsKey(scriptName))
            {
                scriptName = baseName + " " + i++;
            }
            Script script = new Script(scriptName, null, false, null);
            Personality.Scripts.Add(script.Name, script);

            // Now fire up an edit
            EditScriptWindow editScriptWindow = new EditScriptWindow(Personality.Scripts, script.Name);
            if (editScriptWindow.ShowDialog() == true)
            {
                Personality.ToFile();
                EDDI.Core.Eddi.Instance.Reload("Speech responder");
            }
            else
            {
                Personality.Scripts.Remove(script.Name);
            }
            scriptsData.Items.Refresh();
        }

        private void CopyPersonalityClicked(object sender, RoutedEventArgs e)
        {
            CopyPersonalityWindow window = new CopyPersonalityWindow(Personality);
            if (window.ShowDialog() == true)
            {
                string personalityName = window.PersonalityName == null ? null : window.PersonalityName.Trim();
                string personalityDescription = window.PersonalityDescription == null ? null : window.PersonalityDescription.Trim();
                Personality newPersonality = Personality.Copy(personalityName, personalityDescription);
                Personalities.Add(newPersonality);
                Personality = newPersonality;
            }
        }

        private void DeletePersonalityClicked(object sender, RoutedEventArgs e)
        {
            string messageBoxText = "Are you sure you want to delete the \"" + Personality.Name + "\" personality?";
            string caption = "Delete Personality";
            MessageBoxResult result = MessageBox.Show(messageBoxText, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Remove the personality from the list and the local filesystem
                    Personality oldPersonality = Personality;
                    Personalities.Remove(oldPersonality);
                    Personality = Personalities[0];
                    oldPersonality.RemoveFile();
                    break;
            }
        }

        private void SubtitlesEnabled(object sender, RoutedEventArgs e)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            configuration.Subtitles = true;
            configuration.ToFile();
            EDDI.Core.Eddi.Instance.Reload("Speech responder");
        }

        private void SubtitlesDisabled(object sender, RoutedEventArgs e)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            configuration.Subtitles = false;
            configuration.ToFile();
            EDDI.Core.Eddi.Instance.Reload("Speech responder");
        }

        private void SubtitlesOnlyEnabled(object sender, RoutedEventArgs e)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            configuration.SubtitlesOnly = true;
            configuration.ToFile();
            EDDI.Core.Eddi.Instance.Reload("Speech responder");
        }

        private void SubtitlesOnlyDisabled(object sender, RoutedEventArgs e)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            configuration.SubtitlesOnly = false;
            configuration.ToFile();
            EDDI.Core.Eddi.Instance.Reload("Speech responder");
        }

        private void PersonalityDefaultTxt(Personality personality)
        {
            if (personality.IsDefault)
            {
                defaultText.Text = "The default personality cannot be modified. If you wish to make changes, create a copy.";
                defaultText.FontWeight = FontWeights.Bold;
                defaultText.FontStyle = FontStyles.Italic;
                defaultText.FontSize = 13;
            }
            else
            {
                defaultText.Text = "Scripts which are triggered by an event can be disabled but not deleted.";
                defaultText.FontWeight = FontWeights.Normal;
                defaultText.FontStyle = FontStyles.Italic;
                defaultText.FontSize = 13;
            }
        }
    }
}
