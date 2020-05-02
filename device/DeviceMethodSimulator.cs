using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace egeorge.iot.directmethod
{
    public class DirectMethodSimulator
    {
        DeviceClient iotHubClinet;

        public DirectMethodSimulator(string connectionString, TransportType transporttype= TransportType.Amqp)
        {
            iotHubClinet = DeviceClient.CreateFromConnectionString(connectionString, transporttype);
        }

        public async Task Connect()
        {
            try
            {
                await iotHubClinet.OpenAsync();
                Debug.WriteLine("IoT Hub connection opened.");
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
        }

        public async Task Disconnect()
        {
            await iotHubClinet.CloseAsync();
            Debug.WriteLine("IoT Hub connection closed.");
        }

        public async Task SetDirectMethods()
        {
            await iotHubClinet.SetMethodDefaultHandlerAsync(DirectMethodCallback, this);
        }

        public async Task SetDeviceTwins()
        {
            var twin = await iotHubClinet.GetTwinAsync();
            var reportedProps = new ReportedProperty(){
                DeviceType = "method-invocation-test-device"
            };
            var reportedPropTwin = new TwinCollection(Newtonsoft.Json.JsonConvert.SerializeObject(reportedProps));
            await iotHubClinet.UpdateReportedPropertiesAsync(reportedPropTwin);
            string desiredProps = twin.Properties.Desired.ToJson();
            UpdateDesiredTwinPropery(desiredProps);

            await iotHubClinet.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateHandler, this);
        }

        public async Task DesiredPropertyUpdateHandler(TwinCollection desiredProperties, object userContext)
        {
            await Task.Run(()=>{
                UpdateDesiredTwinPropery(desiredProperties.ToJson());
            });
        }

        void UpdateDesiredTwinPropery(string json)
        {
            dynamic twinPropsJson = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            var directMethodTestProps = twinPropsJson["direct-method-test"] as Newtonsoft.Json.Linq.JObject;
            if (!(directMethodTestProps is null))
            {
                SleepTimeInMethod = (int)directMethodTestProps.GetValue("sleep-time");
                ResponseDataLength = (int)directMethodTestProps.GetValue("response-data-length");
            }
        }

        private int? SleepTimeInMethod { get; set; }
        private int? ResponseDataLength { get; set; }

        public async Task<MethodResponse> DirectMethodCallback(MethodRequest methodRequest, object userContext)
        {
            var numChars = new char[] {'0','1','2','3','4','5','6','7','8','9'};
            var startTime = DateTime.Now;
            var methodName = methodRequest.Name;
            dynamic payload = Newtonsoft.Json.JsonConvert.DeserializeObject(methodRequest.DataAsJson);
            Debug.WriteLine("Invocation Received - {0} at {1}",methodName, startTime);
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

            Debug.WriteLine("Invocation Done - {0} at {1}",methodName, endTime);

            return response;
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
