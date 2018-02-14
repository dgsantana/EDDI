using System.Collections.Generic;
using System.Windows.Controls;
using EddiEvents;
using Newtonsoft.Json.Linq;

namespace EDDI.Core
{
    /// <summary>
    /// The methods required for an EDDI monitor.
    /// </summary>
    public interface IEDDIMonitor
    {
        /// <summary>
        /// A short name for the monitor
        /// </summary>
        string MonitorName { get; }

        /// <summary>
        /// The version of the monitor
        /// </summary>
        string MonitorVersion { get; }

        /// <summary>
        /// A brief description of the monitor
        /// </summary>
        string MonitorDescription { get; }

        /// <summary>
        /// If the monitor is required to be running
        /// </summary>
        bool IsRequired { get; }

        /// <summary>
        /// If the monitor needs to be explicitly started/stopped
        /// </summary>
        bool NeedsStart { get; }

        /// <summary>
        /// Called when this monitor is started.  This is not expected to return whilst the monitor is running
        /// </summary>
        void Start();

        /// <summary>
        /// Called when this monitor is stopped.  This should tell the monitor to shut down
        /// </summary>
        void Stop();

        /// <summary>
        /// Called when this monitor needs to reload its configuration
        /// </summary>
        void Reload();

        /// <summary>
        /// Called prior to responders running.  This should be used to update state
        /// </summary>
        void PreHandle(Event ev);

        /// <summary>
        /// Called after responders running.  This should be used to generate follow-on events
        /// </summary>
        void PostHandle(Event ev);

        /// <summary>
        /// Called to receive information from the Frontier API
        /// </summary>
        void HandleProfile(JObject profile);

        /// <summary>
        /// Provide any local variables
        /// </summary>
        IDictionary<string, object> GetVariables();

        UserControl ConfigurationTabItem();
    }
}
