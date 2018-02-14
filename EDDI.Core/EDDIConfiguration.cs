using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using Utilities;

namespace EDDI
{
    /// <summary>Configuration for EDDI</summary>
    public class EDDIConfiguration
    {
        [JsonProperty("homeSystem")]
        public string HomeSystem { get; set; }
        [JsonProperty("homeStation")]
        public string HomeStation { get; set; }
        [JsonProperty("debug")]
        public bool Debug { get; set; }
        [JsonProperty("beta")]
        public bool Beta { get; set; }
        [JsonProperty("insurance")]
        public decimal Insurance { get; set; }
        [JsonProperty("plugins")]
        public IDictionary<string, bool> Plugins { get; set; }
        [JsonProperty("Gender")]
        public string Gender { get; set; }

        /// <summary>the current export target for the shipyard</summary>
        [JsonProperty("exporttarget")]
        public string ExportTarget { get; set; }

        [JsonProperty("errorReporting")] public bool ErrorReporting { get; set; }

        [JsonIgnore]
        private string _dataPath;

        public EDDIConfiguration()
        {
            Debug = false;
            Beta = false;
            Insurance = 5;
            Plugins = new Dictionary<string, bool>();
            ExportTarget = "Coriolis";
            Gender = "Male";
        }

        static EDDIConfiguration()
        {
            if (!Directory.Exists(Constants.DATA_DIR))
                Directory.CreateDirectory(Constants.DATA_DIR);
            var watcher = new FileSystemWatcher(Constants.DATA_DIR, "eddi.json") {EnableRaisingEvents = true};
            OnConfigurationChange = Observable.Create<EDDIConfiguration>(x =>
                {
                    var fswChanged = Observable.FromEvent<FileSystemEventHandler, FileSystemEventArgs>(handler =>
                        {
                            void FsHandler(object sender, FileSystemEventArgs e)
                            {
                                handler(e);
                            }

                            return FsHandler;
                        },
                        fsHandler => watcher.Changed += fsHandler,
                        fsHandler => watcher.Changed -= fsHandler);
                    return fswChanged.Subscribe(y=>
                    {
                        if(!_saving)
                            x.OnNext(FromFile());
                    });
                });
        }

        public static IObservable<EDDIConfiguration> OnConfigurationChange;
        private static bool _saving;

        /// <summary>
        /// Obtain configuration from a file.  If the file name is not supplied the the default
        /// path of Constants.Data_DIR\eddi.json is used
        /// </summary>
        public static EDDIConfiguration FromFile(string filename=null)
        {
            if (filename == null)
            {
                filename = Constants.DATA_DIR + @"\eddi.json";
            }

            var configuration = new EDDIConfiguration();
            if (File.Exists(filename))
            {
                try
                {
                    string data = Files.Read(filename);
                    if (data != null)
                    {
                        configuration = JsonConvert.DeserializeObject<EDDIConfiguration>(data);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Debug("EDDI configuration file could not be read", ex);
                }

            }
            if (configuration == null)
            {
                configuration = new EDDIConfiguration();
            }

            configuration._dataPath = filename;
            if (configuration.Plugins == null)
            {
                configuration.Plugins = new Dictionary<string, bool>();
            }

            return configuration;
        }

        /// <summary>
        /// Write configuration to a file.  If the filename is not supplied then the path used
        /// when reading in the configuration will be used, or the default path of 
        /// Constants.Data_DIR\eddi.json will be used
        /// </summary>
        public void ToFile(string filename=null)
        {
            _saving = true;
            if (filename == null)
                filename = _dataPath;
            if (filename == null)
                filename = Constants.DATA_DIR + @"\eddi.json";

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            Files.Write(filename, json);
            _saving = false;
        }
    }
}
