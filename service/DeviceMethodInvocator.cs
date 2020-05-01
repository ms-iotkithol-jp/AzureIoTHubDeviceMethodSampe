using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.Devices;

using MathNet.Numerics.Statistics;
using Newtonsoft.Json;

namespace egeorge.iot.devicemethod
{
    public class DeviceMethodInvocator
    {
        string iotHubConnectionString;
        TransportType iotHubTransportType;
        ServiceClient serviceClient;
        public LogWriter Log {get; set;}
        public DeviceMethodInvocator(string connectionString, TransportType transportType=TransportType.Amqp)
        {
            iotHubConnectionString = connectionString;
            iotHubTransportType = transportType;
        }

        public async Task Connect()
        {
            serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString, iotHubTransportType);
            await serviceClient.OpenAsync();
        }

        public async Task Disconnect()
        {
            await serviceClient.CloseAsync();
        }

        public async Task TryDeviceMethodInvocation(string deviceId, DeviceMethodInvocationSpec spec, CancellationToken ct)
        {
            if (Log==null)
            {
                Log = new DefaultLogWriter();
            }

            cancellationToken = ct;
            var dataUnits = new char[] {'0','1','2','3','4','5','6','7','8','9'};
            var sb = new StringBuilder();
            for (var i=0;i<spec.SizeOfDataInPayload;i++)
            {
                sb.Append(dataUnits[i%dataUnits.Length]);
            }

            var invokeDeltas = new List<double>();
            for (var i=0;i<spec.NumOfInvocationLoop;i++)
            {
                CloudToDeviceMethod deviceMethod = new CloudToDeviceMethod(spec.MethodName);
                deviceMethod.ResponseTimeout = TimeSpan.FromMilliseconds(spec.ResponseTimeout);
                var payload = sb.ToString();
                if (!string.IsNullOrEmpty(payload))
                {
                    var json = new {
                        data = System.Text.Encoding.UTF8.GetBytes(payload)
                    };
                    deviceMethod.SetPayloadJson(Newtonsoft.Json.JsonConvert.SerializeObject(json));
                }
                bool succeeded = false;
                var startTimestamp = DateTime.Now;
                try {
                    CloudToDeviceMethodResult resultOfInvocation = null;
                    if (!string.IsNullOrEmpty(spec.ModuleId))
                    {
                        resultOfInvocation = await serviceClient.InvokeDeviceMethodAsync(deviceId,spec.ModuleId, deviceMethod);
                    }
                    else{
                        resultOfInvocation =  await serviceClient.InvokeDeviceMethodAsync(deviceId, deviceMethod);
                    }
                    if (resultOfInvocation.Status == 200 )
                    {
                        dynamic resultJson = Newtonsoft.Json.JsonConvert.DeserializeObject(resultOfInvocation.GetPayloadAsJson());

                        succeeded = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write(string.Format("Exception happend - {0}", ex.Message));
                }
                var endTimeStamp = DateTime.Now;
                if (succeeded)
                {
                    var delta = (float)(endTimeStamp.Subtract(startTimestamp).Ticks/10)/1000.0;
                    delta -= spec.MSecOfWaitTimeInDeviceMethod;
                    Log.Write(string.Format("[{0}]start:{1}->end:{2} = {3}",i, startTimestamp.ToString("yyyy/MM/dd-HH:mm:ss.fff"), endTimeStamp.ToString("yyyy/MM/dd-HH:mm:ss.fff"), delta));
                    invokeDeltas.Add(delta);
                }
                Log.Write(string.Format("Sleeping fo next incocation - {0}",spec.MSecOfInvocationInterval));
                if (ct.IsCancellationRequested)
                {
                    break;
                }
                await Task.Delay(spec.MSecOfInvocationInterval);
            }
            Log.Write("Statistics");
            Log.Write(string.Format("Condition - InvokePayloadSize={0},ResponseDataSize={1},SleepInMethod={2},InvokeInterval={3}",spec.SizeOfDataInPayload, spec.SizeOfResponseData, spec.MSecOfWaitTimeInDeviceMethod, spec.MSecOfInvocationInterval));
            Log.Write(string.Format("Condition - transport={0}", iotHubTransportType.ToString()));
            Log.Write(string.Format("Total Incation Count - {0}", spec.NumOfInvocationLoop));
            Log.Write(string.Format("Succeeded Count - {0}", invokeDeltas.Count));
            Log.Write(string.Format("Mean:{0},Median:{1},PSD:{2}",invokeDeltas.Mean(), invokeDeltas.Median(), invokeDeltas.PopulationStandardDeviation()));
        }

        CancellationToken cancellationToken;


    }

    public class DeviceMethodInvocationSpec
    {
        public string TestDeviceType { get; set; }
        public string MethodName { get; set; }
        public int SizeOfDataInPayload { get; set; }
        public int MSecOfWaitTimeInDeviceMethod { get; set; }
        public int NumOfInvocationLoop { get ; set; }

        public int MSecOfInvocationInterval { get; set; }
        public int SizeOfResponseData { get; set; }
        public int ResponseTimeout { get; set; }

        public string ModuleId { get; set;}
    }

    public class DeviceTwinDesiredProperty
    {
        [JsonProperty("sleep-time")] public int SleepTime { get; set; }
        [JsonProperty("response-data-length")] public int ReponseDataLength { get; set; }
    }

    class DefaultLogWriter : LogWriter
    {
        public void Write(string content)
        {
            Debug.WriteLine(content);
        }
    }
}