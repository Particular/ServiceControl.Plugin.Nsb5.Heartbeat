namespace ServiceControl.Plugin.Nsb5.Heartbeat
{
    using System;
    using System.Configuration;
    using System.Threading;
    using System.Threading.Tasks;
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
            
            backend = new ServiceControlBackend(SendMessages, Configure);
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

            NotifyEndpointStartup(hostInfo, DateTime.UtcNow);
            StartHeartbeats(hostInfo);
        }

        void NotifyEndpointStartup(HostInformation hostInfo, DateTime startupTime)
        {
            try
            {
                backend.Send(
                    new RegisterEndpointStartup
                    {
                        HostId = hostInfo.HostId,
                        Host = hostInfo.DisplayName,
                        Endpoint = Configure.Settings.EndpointName(),
                        HostDisplayName = hostInfo.DisplayName,
                        HostProperties = hostInfo.Properties,
                        StartedAt = startupTime
                    }, ttlTimeSpan);
            }
            catch (Exception ex)
            {
                logger.Warn(string.Format("Unable to register endpoint startup with ServiceControl. Going to reattempt registration after {0}.", registrationRetryInterval), ex);

                Task.Delay(registrationRetryInterval).ContinueWith(t => NotifyEndpointStartup(hostInfo, startupTime));
            }
        }

        void StartHeartbeats(HostInformation hostInfo)
        {
            logger.DebugFormat("Start sending heartbeats every {0}", heartbeatInterval);
            heartbeatTimer = new Timer(x => ExecuteHeartbeat(hostInfo), null, TimeSpan.Zero, heartbeatInterval);
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

            try
            {
                backend.Send(heartBeat, ttlTimeSpan);
            }
            catch (Exception ex)
            {
                logger.Warn("Unable to send heartbeat to ServiceControl:", ex);
            }
        }

        ServiceControlBackend backend;
        // ReSharper disable once NotAccessedField.Local
        Timer heartbeatTimer;
        TimeSpan heartbeatInterval;
        TimeSpan ttlTimeSpan;
        TimeSpan registrationRetryInterval = TimeSpan.FromMinutes(1);

        protected override void Setup(FeatureConfigurationContext context)
        {
        }
        
    }
}