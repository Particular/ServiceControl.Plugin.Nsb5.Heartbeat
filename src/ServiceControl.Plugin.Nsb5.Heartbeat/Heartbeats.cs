namespace ServiceControl.Plugin.Nsb5.Heartbeat
{
    using System;
    using System.Configuration;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Config;
    using NServiceBus.Hosting;
    using NServiceBus.Logging;
    using NServiceBus.Transports;
    using NServiceBus.Unicast;
    using ServiceControl.Plugin.Heartbeat.Messages;

    class Heartbeats : IWantToRunWhenConfigurationIsComplete, IDisposable
    {
        private const int MillisecondsToWaitForShutdown = 500;
        static ILog Logger = LogManager.GetLogger(typeof(Heartbeats));

        public Heartbeats(ISendMessages sendMessages, Configure configure, UnicastBus unicastBus)
        {
            this.unicastBus = unicastBus;

            backend = new ServiceControlBackend(sendMessages, configure);
            endpointName = configure.Settings.EndpointName();

            var interval = ConfigurationManager.AppSettings["Heartbeat/Interval"];
            if (!String.IsNullOrEmpty(interval))
            {
                heartbeatInterval = TimeSpan.Parse(interval);
            }

            ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks*4); // Default ttl
            var ttl = ConfigurationManager.AppSettings["Heartbeat/TTL"];
            if (!String.IsNullOrWhiteSpace(ttl))
            {
                if (TimeSpan.TryParse(ttl, out ttlTimeSpan))
                {
                    Logger.InfoFormat("Heartbeat/TTL set to {0}", ttlTimeSpan);
                }
                else
                {
                    ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks*4);
                    Logger.Warn("Invalid Heartbeat/TTL specified in AppSettings. Reverted to default TTL (4 x Heartbeat/Interval)");
                }
            }
        }

        public void Dispose()
        {
            if (heartbeatTimer != null)
            {
                using (var manualResetEvent = new ManualResetEvent(false))
                {
                    heartbeatTimer.Dispose(manualResetEvent);
                    manualResetEvent.WaitOne(MillisecondsToWaitForShutdown);
                }
            }

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }
        }

        public void Run(Configure config)
        {
            cancellationTokenSource = new CancellationTokenSource();

            NotifyEndpointStartup(unicastBus.HostInformation, DateTime.UtcNow);
            StartHeartbeats(unicastBus.HostInformation);
        }


        void NotifyEndpointStartup(HostInformation hostInfo, DateTime startupTime)
        {
            // don't block here since StartupTasks are executed synchronously.
            Task.Run(() => SendEndpointStartupMessage(hostInfo, startupTime, cancellationTokenSource.Token));
        }

        void StartHeartbeats(HostInformation hostInfo)
        {
            Logger.DebugFormat("Start sending heartbeats every {0}", heartbeatInterval);
            heartbeatTimer = new Timer(x => SendHeartbeatMessage(hostInfo), null, TimeSpan.Zero, heartbeatInterval);
        }

        void SendEndpointStartupMessage(HostInformation hostInfo, DateTime startupTime, CancellationToken cancellationToken)
        {
            try
            {
                backend.Send(
                    new RegisterEndpointStartup
                    {
                        HostId = hostInfo.HostId,
                        Host = hostInfo.DisplayName,
                        Endpoint = endpointName,
                        HostDisplayName = hostInfo.DisplayName,
                        HostProperties = hostInfo.Properties,
                        StartedAt = startupTime
                    }, ttlTimeSpan);
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("Unable to register endpoint startup with ServiceControl. Going to reattempt registration after {0}.", registrationRetryInterval), ex);

                Task.Delay(registrationRetryInterval, cancellationToken)
                    .ContinueWith(t => SendEndpointStartupMessage(hostInfo, startupTime, cancellationToken), cancellationToken);
            }
        }

        void SendHeartbeatMessage(HostInformation hostInfo)
        {
            var heartBeat = new EndpointHeartbeat
            {
                ExecutedAt = DateTime.UtcNow,
                EndpointName = endpointName,
                Host = hostInfo.DisplayName,
                HostId = hostInfo.HostId
            };

            try
            {
                backend.Send(heartBeat, ttlTimeSpan);
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug("Ignoring object disposed. Likely means we are shutting down:", ex);
            }
            catch (Exception ex)
            {
                Logger.Warn("Unable to send heartbeat to ServiceControl:", ex);
            }
        }

        readonly UnicastBus unicastBus;

        ServiceControlBackend backend;
        CancellationTokenSource cancellationTokenSource;
        string endpointName;
        TimeSpan heartbeatInterval = TimeSpan.FromSeconds(10);
        Timer heartbeatTimer;
        TimeSpan registrationRetryInterval = TimeSpan.FromMinutes(1);
        TimeSpan ttlTimeSpan;
    }
}