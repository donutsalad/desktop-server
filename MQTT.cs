using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json;

namespace MQTT
{
    public class Server : IDisposable
    {
        #region CONSTANTS
        public const string SYSTEM_MESSAGE_CHANNEL = "system";
        public const string SYSTEM_SHUTDOWN_MESSAGE = "shutdown";
        public const string SYSTEM_RETAIN_FILE = "retain.json";
        #endregion

        public MqttServer mqttServer { get; }
        public Task startupTask { get; }

        #region EVENTS
        public event ServerShutdownHandler OnShutdown;
        public delegate void ServerShutdownHandler (object sender, MqttServerShutdownEventArgs e);
        public struct MqttServerShutdownEventArgs {
            public enum ShutdownCause
            {
                Unknown,
                Deconstructor,
                Application,
                Internal,
                UnhandledException
            }

            public readonly ShutdownCause Cause;

            public MqttServerShutdownEventArgs()
            {
                Cause = ShutdownCause.Unknown;
            }

            public MqttServerShutdownEventArgs(ShutdownCause cause)
            {
                Cause = cause;
            }
        }
        
        public event MessagePublishedHandler OnMessagePublished;
        public delegate void MessagePublishedHandler (object sender, MqttMessageReceivedEventArgs e);
        public struct MqttMessageReceivedEventArgs
        {
            public MqttApplicationMessage message;
            public string clientID;

            public string topic => message.Topic;
            public string content => message.ContentType;
            public byte[] payload => message.Payload;
            public string payloadAsString => message.ConvertPayloadToString();

            public MqttMessageReceivedEventArgs(MqttApplicationMessage message, string clientID)
            {
                this.message = message;
                this.clientID = clientID;
            }
        }
        #endregion
        
        public Server()
        {
            OnShutdown += ShutdownBroadcast;
            OnMessagePublished += LogMessage;

            mqttServer = new MqttFactory().CreateMqttServer(new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .Build()
            );

            startupTask = ServerStartup();
        }

        ~Server() => Dispose();

        public void Dispose()
        {
            PersistRetain().Wait();

            if(mqttServer.IsStarted)
                ServerShutdown(MqttServerShutdownEventArgs.ShutdownCause.Deconstructor);

            Console.WriteLine("Destroying Server");
        }

        
        #region TRACKCLIENTS
        private async Task ValidateConnection(ValidatingConnectionEventArgs args)
        {
            //TEMPORARY DEBUG CODE:

            if(args.ClientId == "rejectme")
            {
                args.ReasonCode = MqttConnectReasonCode.Banned;
                return;
            }

            args.ReasonCode = MqttConnectReasonCode.Success;
        }
        
        private async Task ClientConnected(ClientConnectedEventArgs args)
        {
            //Track Clients eventually.
        }

        private async Task ClientDisconnected(ClientDisconnectedEventArgs args)
        {
            //Track Clients eventually.
        }

        private async Task ClientSubscribedTopic(ClientSubscribedTopicEventArgs args)
        {
            //Track Clients eventually.
        }

        private async Task ClientUnsubscribedTopic(ClientUnsubscribedTopicEventArgs args)
        {
            //Track Clients eventually.
        }
        #endregion
        
        #region MESSAGES
        private async Task RouteIncomingMessage(InterceptingPublishEventArgs args)
        {
            if (args.ApplicationMessage.Topic == SYSTEM_MESSAGE_CHANNEL)
                return;
            
            Task global = Task.Run(() => OnMessagePublished.Invoke(this, new MqttMessageReceivedEventArgs(args.ApplicationMessage, args.ClientId)));

            //Route on topic/client data.

            await global;
        }

        public async void SendPayload(string topic, byte[] payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.ExactlyOnce, bool retain = false)
        {
            await mqttServer.InjectApplicationMessage(new MqttInjectedApplicationMessage(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .WithRetainFlag(retain)
                    .Build()
                )
            );
        }

        public async void SendMessage(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false)
        {
            await mqttServer.InjectApplicationMessage(new MqttInjectedApplicationMessage(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .WithRetainFlag(retain)
                    .Build()
                )
            );
        }
        #endregion
        
        #region PERSIST
        private async Task PersistRetain()
        {
            await File.WriteAllTextAsync(
                Path.Combine(Directory.GetCurrentDirectory(), SYSTEM_RETAIN_FILE), 
                JsonConvert.SerializeObject(await mqttServer.GetRetainedMessagesAsync())
            );
        }

        private async Task<IList<MqttApplicationMessage>> PersistRecover(LoadingRetainedMessagesEventArgs args)
        {
            string file = Path.Combine(Directory.GetCurrentDirectory(), SYSTEM_RETAIN_FILE);

            if(!File.Exists(file))
                return new List<MqttApplicationMessage>(); //Returning null okay?? not checking rn.

            try
            {
                Console.WriteLine("Recovering retained messages...");
                IList<MqttApplicationMessage> messages = await File.ReadAllTextAsync(file).ContinueWith(t => 
                    JsonConvert.DeserializeObject<IList<MqttApplicationMessage>>(t.Result) 
                    ?? new List<MqttApplicationMessage>()
                );
                Console.WriteLine("Recovered retained messages.");
                return messages;
            }
            catch(Exception e)
            {
                Console.WriteLine("WARNING! Creating backup and clearing retain file. Error whilst recovering retained messages: {0}", e.Message);
                File.Move(file, string.Format(file + "-{0:yyyy-MM-dd_HH-mm-ss}.bak", DateTime.Now));
                return new List<MqttApplicationMessage>();
            }
        }
        #endregion
        
        private async Task ServerStartup()
        {
            mqttServer.LoadingRetainedMessageAsync += PersistRecover;
            mqttServer.ValidatingConnectionAsync += ValidateConnection;
            mqttServer.ClientConnectedAsync += ClientConnected;
            mqttServer.ClientDisconnectedAsync += ClientDisconnected;
            mqttServer.ClientSubscribedTopicAsync += ClientSubscribedTopic;
            mqttServer.ClientUnsubscribedTopicAsync += ClientUnsubscribedTopic;
            mqttServer.InterceptingPublishAsync += RouteIncomingMessage;
            await mqttServer.StartAsync();
            Console.WriteLine("Server is currently {0}.", mqttServer.IsStarted ? "running" : "not running");
        }

        public async void ServerShutdown(MqttServerShutdownEventArgs.ShutdownCause cause = MqttServerShutdownEventArgs.ShutdownCause.Unknown)
        {
            OnShutdown.Invoke(this, new MqttServerShutdownEventArgs(cause));
            await mqttServer.StopAsync();
        }
        
        private async void ShutdownBroadcast(object sender, MqttServerShutdownEventArgs e)
        {
            await mqttServer.InjectApplicationMessage(new MqttInjectedApplicationMessage(
                new MqttApplicationMessageBuilder()
                    .WithTopic(SYSTEM_MESSAGE_CHANNEL)
                    .WithPayload(SYSTEM_SHUTDOWN_MESSAGE)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .Build()
                )
            );
        }

        private async void LogMessage(object sender, MqttMessageReceivedEventArgs args)
        {
            Console.WriteLine("{0} | Dumping Incoming MQTT Message:\nTopic: {1} | Message:\n{2}", DateTime.Now, args.topic, args.payloadAsString);
        }
    }
}