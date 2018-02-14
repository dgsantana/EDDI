using Newtonsoft.Json;
using System.ComponentModel;

namespace EddiSpeechResponder
{
    public class Script : INotifyPropertyChanged
    {
        [JsonProperty("name")]
        public string Name { get; private set; }
        [JsonProperty("description")]
        public string Description { get; set; }
        [JsonProperty("enabled")]
        private bool _enabled;
        [JsonProperty("priority")]
        private int _priority = 3;
        [JsonProperty("responder")]
        private bool _responder;
        [JsonProperty("script")]
        private string _script;
        [JsonProperty("default")]
        private bool _isDefault;

        public event PropertyChangedEventHandler PropertyChanged;

        [JsonIgnore]
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value;  OnPropertyChanged("Enabled"); }
        }

        [JsonIgnore]
        public int Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged("Priority"); }
        }

        [JsonIgnore]
        public bool Responder
        {
            get => _responder;
            private set { _responder = value; OnPropertyChanged("Responder"); }
        }

        [JsonIgnore]
        public bool Default
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged("Default"); }
        }

        [JsonIgnore]
        public bool IsDeleteable => !_responder;

        [JsonIgnore]
        public string Value
        {
            get => _script;
            set { _script = value; OnPropertyChanged("Value"); }
        }

        [JsonIgnore]
        public bool HasValue => _script != null;

        public Script(string name, string description, bool responder, string script, int priority = 3, bool Default = false)
        {
            Name = name;
            Description = description;
            _responder = responder;
            Value = script;
            Priority = priority;
            Enabled = true;
            this.Default = Default;
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
