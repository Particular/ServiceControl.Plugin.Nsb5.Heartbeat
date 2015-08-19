namespace ServiceControl.Plugin.Nsb5.Heartbeat.AcceptanceTests
{
    using System;
    using System.Configuration;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;

    public class When_using_a_custom_circuit_breaker_timeout
    {
        [Test]
        public void Should_honor_configured_value()
        {
            var context = new Context();

            Scenario.Define(context).WithEndpoint<EndpointWithCustomCBSettings>(b => b
                .CustomConfig(busConfig => busConfig
                    .DefineCriticalErrorAction((s, exception) =>
                    {
                        context.Message = s;
                        context.CriticalExceptionReceived = true;
                    })))
                .AllowExceptions()
                .Done(c => c.CriticalExceptionReceived)
                .Run(TimeSpan.FromSeconds(10)); // since the default CircuitBreaker timeout is 2 minutes, the test would fail if the configured value isn't picked up.

            StringAssert.StartsWith("This endpoint is repeatedly unable to contact the ServiceControl backend to report endpoint information.", context.Message);
        }

        class EndpointWithCustomCBSettings : EndpointConfigurationBuilder
        {
            public EndpointWithCustomCBSettings()
            {
                EndpointSetup<DefaultServer>();
                // couldn't find a better way to configure the plugin settings. This will probably fail other tests in this project.
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = "invalidSCQueue";
                ConfigurationManager.AppSettings[@"ServiceControl/Heartbeat/CircuitBreakerTimeoutSeconds"] = "1";
            }
        }

        class Context : ScenarioContext
        {
            public bool CriticalExceptionReceived { get; set; }

            public string Message { get; set; }
        }
    }
}