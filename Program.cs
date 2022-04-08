//Testing the MQTT Server Class
using(MQTT.Server TestServer = new MQTT.Server())
{
    TestServer.OnShutdown += (object sender, MQTT.Server.MqttServerShutdownEventArgs e) => {
        Console.WriteLine("Server shutdown, {0} cause", e.Cause);
    };
    Console.WriteLine("Starting server");
    TestServer.startupTask.Wait();
    Console.WriteLine("Server started");
    TestServer.SendMessage("system", "Test Message");
    TestServer.SendMessage("broadcast", "ping", MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce, false);
    TestServer.SendMessage("broadcast", "persistent-test", MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce, true);
    Thread.Sleep(5000);
}