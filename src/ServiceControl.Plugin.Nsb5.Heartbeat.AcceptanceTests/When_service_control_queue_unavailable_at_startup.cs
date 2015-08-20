namespace ServiceControl.Plugin.Nsb5.Heartbeat.AcceptanceTests
{
    using System;
    using System.Configuration;
    using System.Messaging;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTesting.Support;
    using NUnit.Framework;

    public class When_service_control_queue_unavailable_at_startup
    {
        [Test]
        public void Should_honor_configured_value()
        {
            var context = new Context();

            var exception = Assert.Throws<AggregateException>(() =>
                Scenario.Define(context).WithEndpoint<EndpointWithMissingSCQueue>(b => b
                    .CustomConfig(busConfig => busConfig
                        .DefineCriticalErrorAction((s, e) =>
                        {
                            context.CriticalExceptionReceived = true;
                        })))
                    .AllowExceptions()
                    .Run());

            // we currently can't test for the InvalidOperationException thrown by the ServiceControlBackend
            // class since that gets cut away in the EndpointRunner.
            Assert.IsInstanceOf<ScenarioException>(exception.InnerException);
            Assert.AreEqual("Endpoint EndpointWithMissingSCQueue failed to initialize", exception.InnerException.Message);
            Assert.IsInstanceOf<MessageQueueException>(exception.InnerException.InnerException);
            Assert.IsFalse(context.CriticalExceptionReceived);
        }

        class EndpointWithMissingSCQueue : EndpointConfigurationBuilder
        {
            public EndpointWithMissingSCQueue()
            {
                EndpointSetup<DefaultServer>();
                // couldn't find a better way to configure the plugin settings. This will probably fail other tests in this project.
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = "invalidSCQueue";
            }
        }

        class Context : ScenarioContext
        {
            public bool CriticalExceptionReceived { get; set; }
        }
    }
}