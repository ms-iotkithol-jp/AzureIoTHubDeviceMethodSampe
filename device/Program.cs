﻿using System;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Azure.Devices.Client;
using System.Threading.Tasks;

namespace egeorge.iot.directmethod
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false);
            app.Name = "directmethodsimulator";
            app.HelpOption("-h|--help");

            var csOption = app.Option(
                "--connection-string",
                "Specify connection string for device of IoT Hub",
                CommandOptionType.SingleValue
            );

            var ttOption = app.Option(
                "--transport",
                "Specify transport type - [amqp|mqtt|http]",
                CommandOptionType.SingleValue
            );

            app.OnExecute(()=>{
                var p = new Program()
                {
                    iotHubConnectionString = csOption.Value(),
                    iotHubTransportType = ttOption.Value()
                };

                try
                {
                    var task = p.DirectMethodInvocationReceiving();
                    task.Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                return 0;
            });

            app.Execute(args);

        }

        string iotHubConnectionString { get; set; }
        string iotHubTransportType { get; set; }

        async Task DirectMethodInvocationReceiving()
        {
            if (string.IsNullOrEmpty(iotHubConnectionString))
            {
                Console.WriteLine("Please specify IoT Hub Connection String for this device.");
                return;
            }
            TransportType[] tts = {TransportType.Amqp, TransportType.Amqp_Tcp_Only, TransportType.Amqp_WebSocket_Only, TransportType.Http1, TransportType.Mqtt, TransportType.Mqtt_Tcp_Only, TransportType.Mqtt_WebSocket_Only};
            TransportType tt = TransportType.Amqp;
            if (! string.IsNullOrEmpty(iotHubTransportType))
            {
                foreach (var t in tts)
                {
                    if (t.ToString().ToLower().StartsWith(iotHubTransportType))
                    {
                        tt = t;
                        break;
                    }
                }
            }

            var devSim = new DirectMethodSimulator(iotHubConnectionString, tt);
            await devSim.Connect();
            await devSim.SetDirectMethods();
            await devSim.SetDeviceTwins();
            
//            Console.WriteLine("To stop this aps, Waiting for input any key...");
  //          var rk = Console.ReadKey();
            await Task.Delay(-1);

            await devSim.Disconnect();
        }
    }
}
