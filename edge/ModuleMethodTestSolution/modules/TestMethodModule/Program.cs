namespace TestMethodModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static int counter;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);
            
            await SetupTwins(ioTHubModuleClient);
            await SetupMethodCallback(ioTHubModuleClient);
            
        }

        static async Task SetupTwins(ModuleClient ioTHubModuleClient)
        {
            var twin = await ioTHubModuleClient.GetTwinAsync();
            
            var reportedProps = new ReportedProperty(){
                DeviceType = "method-invocation-test-device"
            };
            var reportedPropTwin = new TwinCollection(Newtonsoft.Json.JsonConvert.SerializeObject(reportedProps));
            await ioTHubModuleClient.UpdateReportedPropertiesAsync(reportedPropTwin);
            string desiredProps = twin.Properties.Desired.ToJson();
            UpdateDesiredTwinPropery(desiredProps);

            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateHandler, ioTHubModuleClient);
        }

        static int? SleepTimeInMethod;
        static int? ResponseDataLength;

        static void UpdateDesiredTwinPropery(string json)
        {
            dynamic twinPropsJson = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            var directMethodTestProps = twinPropsJson["direct-method-test"] as Newtonsoft.Json.Linq.JObject;
            if (!(directMethodTestProps is null))
            {
                SleepTimeInMethod = (int)directMethodTestProps.GetValue("sleep-time");
                ResponseDataLength = (int)directMethodTestProps.GetValue("response-data-length");
                Console.WriteLine("Module Twin updated by {0}",json);
            }
        }

        static async Task DesiredPropertyUpdateHandler(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Update Desired Property request.");
            await Task.Run(()=>{
                UpdateDesiredTwinPropery(desiredProperties.ToJson());
            });
        }


        static async Task SetupMethodCallback(ModuleClient ioTHubModuleClient)
        {
            await ioTHubModuleClient.SetMethodDefaultHandlerAsync(DirectMethodCallback, ioTHubModuleClient);
        }

        static async Task<MethodResponse> DirectMethodCallback(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("DirectMethod is invoked!");
            var numChars = new char[] {'0','1','2','3','4','5','6','7','8','9'};
            var startTime = DateTime.Now;
            var methodName = methodRequest.Name;
            dynamic payload = Newtonsoft.Json.JsonConvert.DeserializeObject(methodRequest.DataAsJson);
            // Debug.WriteLine("Invocation Received - {0} at {1}",methodName, startTime);
            if (!(SleepTimeInMethod is null)){
                await Task.Delay(SleepTimeInMethod.Value);
            }
            string responseData = "invoked";
            if (!(ResponseDataLength is null) )
            {
                var sb = new StringBuilder();
                for (int i=0;i<ResponseDataLength.Value;i++)
                {
                    sb.Append(numChars[i%numChars.Length]);
                }
                responseData = sb.ToString();
            }
            var endTime = DateTime.Now;
            var responsePayload = new ResponsePayload ()
            {
                StartTime = startTime,
                EndTime = endTime,
                ResponseData = responseData
            };
            var responsePayloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(responsePayload);
            var response = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(responsePayloadJson) ,200);

            // Debug.WriteLine("Invocation Done - {0} at {1}",methodName, endTime);

            return response;
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                using (var pipeMessage = new Message(messageBytes))
                {
                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }
                    await moduleClient.SendEventAsync("output1", pipeMessage);
                
                    Console.WriteLine("Received message sent");
                }
            }
            return MessageResponse.Completed;
        }
    }

    public class ReportedProperty
    {
        [JsonProperty("sleep-time")] public int? SleepTime { get; set; }
        [JsonProperty("response-data-length")] public int? ReponseDataLength { get; set; }
        [JsonProperty("device-type")] public string DeviceType{ get; set; }
    }

    public class ResponsePayload
    {
        [JsonProperty("start-timestamp")] public DateTime StartTime { get; set; }
        [JsonProperty("end-timestamp")] public DateTime EndTime { get; set; }
        [JsonProperty("response-data")] public string ResponseData { get; set; }
    }

}
