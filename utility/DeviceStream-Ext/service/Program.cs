﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;

namespace Microsoft.Azure.Devices.Samples
{
    public static class Program
    {
        // The IoT Hub connection string. This is available under the "Shared access policies" in the Azure portal.

        // For this sample either:
        // - pass this value as a command-prompt argument
        // - set the IOTHUB_CONN_STRING_CSHARP environment variable 
        // - create a launchSettings.json (see launchSettings.json.template) containing the variable
        private static string s_connectionString = Environment.GetEnvironmentVariable("IOTHUB_CONN_STRING_CSHARP");

        // ID of the device to interact with.
        // - pass this value as a command-prompt argument
        // - set the DEVICE_ID environment variable 
        // - create a launchSettings.json (see launchSettings.json.template) containing the variable
        private static string s_deviceId = Environment.GetEnvironmentVariable("DEVICE_ID");

        // Local port this sample will open to proxy traffic to a device.
        // - pass this value as a command-prompt argument
        // - set the LOCAL_PORT environment variable 
        // - create a launchSettings.json (see launchSettings.json.template) containing the variable
        private static string s_port = Environment.GetEnvironmentVariable("LOCAL_PORT");

        // Select one of the following transports used by ServiceClient to connect to IoT Hub.
        private static TransportType s_transportType = TransportType.Amqp;
        //private static TransportType s_transportType = TransportType.Amqp_WebSocket_Only;

        public static int Main(string[] args)
        {
            var configFileName = "config.yml";
            if (File.Exists(configFileName))
            {
                using (var reader = new StreamReader(File.OpenRead(configFileName)))
                {
                    var yamlDeserializer = new YamlDotNet.Serialization.Deserializer();
                    var yamlObject = yamlDeserializer.Deserialize(reader);
                    var yamlJsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(yamlObject);
                    dynamic configJson = Newtonsoft.Json.JsonConvert.DeserializeObject(yamlJsonStr);
                    if (!(configJson["service-connection-string"] is null))
                    {
                        s_connectionString = configJson["service-connection-string"];
                    }
                    if (!(configJson["target-device-id"] is null))
                    {
                        s_deviceId = configJson["target-device-id"];
                    }
                    if (!(configJson["local-port"] is null))
                    {
                        s_port = configJson["local-port"];
                    }
                }
            }

            int argDeviceIdIndex = 1;
            int argPortIndex = 2;

            if (string.IsNullOrEmpty(s_connectionString) && args.Length == 3)
            {
                s_connectionString = args[0];
            }
            else
            {
                argDeviceIdIndex--;
                argPortIndex--;
            }

            if (string.IsNullOrEmpty(s_deviceId) && args.Length == 2)
            {
                s_deviceId = args[argDeviceIdIndex];
            }

            if (string.IsNullOrEmpty(s_port) && args.Length == 2)
            {
                s_port = args[argPortIndex];
            }

            if (string.IsNullOrEmpty(s_connectionString) ||
                string.IsNullOrEmpty(s_deviceId) ||
                string.IsNullOrEmpty(s_port))
            {
                Console.WriteLine("Please provide a connection string, device ID and local port");
                Console.WriteLine("Usage: ServiceLocalProxyC2DStreamingSample [iotHubConnString] [deviceId] [localPortNumber]");
                return 1;
            }

            int port = int.Parse(s_port, CultureInfo.InvariantCulture);

            using (ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(s_connectionString, s_transportType))
            {
                var sample = new DeviceStreamSample(serviceClient, s_deviceId, port);
                sample.RunSampleAsync().GetAwaiter().GetResult();
            }

            Console.WriteLine("Done.\n");
            return 0;
        }
    }
}
