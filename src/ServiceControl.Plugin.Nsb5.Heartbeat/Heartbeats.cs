namespace ServiceControl.Plugin.Nsb5.Heartbeat
{
    using System;
    using System.Configuration;
    using System.Threading;
    using NServiceBus;
    using NServiceBus.Config;
    using NServiceBus.Features;
    using NServiceBus.Hosting;
    using NServiceBus.Logging;
    using NServiceBus.Transports;
    using NServiceBus.Unicast;
    using ServiceControl.Plugin.Heartbeat.Messages;

    class Heartbeats : Feature, IWantToRunWhenConfigurationIsComplete
    {
        public ISendMessages SendMessages { get; set; }
        public Configure Configure { get; set; }

        public UnicastBus UnicastBus { get; set; }

        public CriticalError CriticalError { get; set; }

        static ILog logger = LogManager.GetLogger(typeof(Heartbeats));
        
        public Heartbeats()
        {
            EnableByDefault();
        }

        public void Run(Configure config)
        {
            if (!IsEnabledByDefault)
            {
                return;
            }
            
            backend = new ServiceControlBackend(SendMessages, Configure, CriticalError);
            backend.VerifyIfServiceControlQueueExists();
            heartbeatInterval = TimeSpan.FromSeconds(10); // Default interval
            var interval = ConfigurationManager.AppSettings[@"Heartbeat/Interval"];
            
            if (!String.IsNullOrEmpty(interval))
            {
                heartbeatInterval = TimeSpan.Parse(interval);
            }

            ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks * 4); // Default ttl
            var ttl = ConfigurationManager.AppSettings[@"Heartbeat/TTL"];
            if (!String.IsNullOrWhiteSpace(ttl))
            {
                if (TimeSpan.TryParse(ttl, out ttlTimeSpan))
                {
                    logger.InfoFormat("Heartbeat/TTL set to {0}", ttlTimeSpan);
                }
                else
                {
                    ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks * 4);
                    logger.Warn("Invalid Heartbeat/TTL specified in AppSettings. Reverted to default TTL (4 x Heartbeat/Interval)");   
                }
            }
            
            var hostInfo = UnicastBus.HostInformation;

            SendStartupMessageToBackend(hostInfo);

            heartbeatTimer = new Timer(x => ExecuteHeartbeat(hostInfo), null, TimeSpan.Zero, heartbeatInterval);
        }

        void SendStartupMessageToBackend(HostInformation hostInfo)
        {
            backend.Send(
                new RegisterEndpointStartup
                {
                    HostId = hostInfo.HostId,
                    Host = hostInfo.DisplayName,
                    Endpoint = Configure.Settings.EndpointName(),
                    HostDisplayName = hostInfo.DisplayName,
                    HostProperties = hostInfo.Properties,
                    StartedAt = DateTime.UtcNow
                }, ttlTimeSpan);
        }

        void ExecuteHeartbeat(HostInformation hostInfo)
        {
            var heartBeat = new EndpointHeartbeat
            {
                ExecutedAt = DateTime.UtcNow,
                EndpointName = Configure.Settings.EndpointName(),
                Host = hostInfo.DisplayName,
                HostId = hostInfo.HostId
            };
            backend.Send(heartBeat, ttlTimeSpan);
        }

        ServiceControlBackend backend;
        // ReSharper disable once NotAccessedField.Local
        Timer heartbeatTimer;
        TimeSpan heartbeatInterval;
        TimeSpan ttlTimeSpan;

        protected override void Setup(FeatureConfigurationContext context)
        {
        }
        
    }
}