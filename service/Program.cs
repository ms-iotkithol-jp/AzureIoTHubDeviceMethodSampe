using System;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace egeorge.iot.devicemethod
{
    class Program
    {
        string iotHubConnectionString;
        string iotHubTransportType;
        
        string configFileName;
        string LogFileName { get; set; }

        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false);
            app.Name = "devicemethodinvocator";
            app.HelpOption("-h|--help");

            var csOption = app.Option(
                "--connection-string",
                "Specify connection string for Service Role of IoT Hub",
                CommandOptionType.SingleValue
            );

            var cfOption = app.Option(
                "--config",
                "Specify configuration yaml file name",
                CommandOptionType.SingleValue
            );

            var ttOption = app.Option(
                "--transport",
                "Specify transport type - [amqp|]",
                CommandOptionType.SingleValue
            );

            var logOption = app.Option(
                "--log",
                "Specify log file. Invocation statistics result will be stored into the file.",
                CommandOptionType.SingleValue
            );

            app.OnExecute(()=>{
                var p = new Program()
                {
                    iotHubConnectionString = csOption.Value(),
                    iotHubTransportType = ttOption.Value(),
                    configFileName = cfOption.Value(),
                    LogFileName = logOption.Value()
                };

                p.TryInvokeDeviceMethods().Wait();

                return 0;
            });
            app.Execute(args);
        }
         CancellationTokenSource cancellationTokenSource;
        async Task TryInvokeDeviceMethods()
        {

            TransportType[] tts = {TransportType.Amqp, TransportType.Amqp_WebSocket_Only};
            TransportType transportType = TransportType.Amqp;
            if (!string.IsNullOrEmpty(iotHubTransportType))
            {
                if (iotHubTransportType.ToLower().Contains("websocket"))
                {
                    transportType = TransportType.Amqp_WebSocket_Only;
                }
            }

            var spec = new DeviceMethodInvocationSpec();
            var deviceids = ParseConfig(ref spec);
            string query = null;
            if (deviceids.Count>0)
            {
                var sb = new StringBuilder();
                foreach (var devId in deviceids)
                {
                    if (!string.IsNullOrEmpty(sb.ToString()))
                    {
                        sb.Append(",");
                    }
                    sb.Append(devId);
                }
                query = $"DeviceId IN ['{sb.ToString()}']";
            }
            else 
            {
                if (!string.IsNullOrEmpty(spec.TestDeviceType))
                {
                    query = $"properties.reported.device-type = '{spec.TestDeviceType}'";
                }
            }
            if (!string.IsNullOrEmpty(query))
            {
                var jobId = await StartDesiredTwinsJob(query, spec);
                await MonitorJob(jobId);
                await jobClient.CloseAsync();

                if (deviceids.Count==0)
                {
                    var registryManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);
                    await registryManager.OpenAsync();
                    var deviceQuery = registryManager.CreateQuery($"SELECT * FROM devices WHERE {query}");
                    deviceids = new List<string>();
                    while(deviceQuery.HasMoreResults)
                    {
                        var twins = await deviceQuery.GetNextAsTwinAsync();
                        foreach (var twin in twins)
                        {
                            deviceids.Add(twin.DeviceId);
                        }
                    }
                }
            }

            FileLogWriter log = null;
            if (!string.IsNullOrEmpty(LogFileName))
            {
                log = new FileLogWriter(LogFileName);
                log.Open(true);
            }
            cancellationTokenSource = new CancellationTokenSource();
            var tasks = new List<Task>();
            var incovators = new List<DeviceMethodInvocator>();
            foreach (var deviceId in deviceids)
            {
                var invocator = new DeviceMethodInvocator(iotHubConnectionString, transportType);
                if (log !=null)
                {
                    invocator.Log = log;
                }
                await invocator.Connect();
                var task = Task.Run( async ()=> {
                     await invocator.TryDeviceMethodInvocation(deviceId, spec, cancellationTokenSource.Token);
                });
          //      task.Start();
                tasks.Add(task);
                incovators.Add(invocator);
            }

            var rk = Console.ReadKey();
            cancellationTokenSource.Cancel();
            Task.WaitAll(tasks.ToArray());
            if (log!=null)
                log.Close();

            foreach( var invocator in incovators)
            {
                await invocator.Disconnect();
            }
        }

        JobClient jobClient;

        async Task<string> StartDesiredTwinsJob(string query, DeviceMethodInvocationSpec spec)
        {
            string jobId = Guid.NewGuid().ToString();
            jobClient = JobClient.CreateFromConnectionString(iotHubConnectionString);
            await jobClient.OpenAsync();

            var desiredTwinJson = new DeviceTwinDesiredProperty()
            {
                SleepTime = spec.MSecOfWaitTimeInDeviceMethod,
                ReponseDataLength = spec.SizeOfResponseData
            };
            var twin = new Twin();
            var json = "{\"device-method-test\":" +  Newtonsoft.Json.JsonConvert.SerializeObject(desiredTwinJson) + "}";
            twin.Properties.Desired = new TwinCollection(json);
            await jobClient.OpenAsync();

            var jobResponse = jobClient.ScheduleTwinUpdateAsync(
                jobId,
                query,
                twin,
                DateTime.UtcNow,
                (long)TimeSpan.FromMinutes(2).TotalSeconds
                ).Result;
            return jobId;
        }

        public async Task MonitorJob(string jobId)
        {
            JobResponse result;
            do
            {
                result = await jobClient.GetJobAsync(jobId);
                Console.WriteLine("Job Status : " + result.Status.ToString());
                await Task.Delay(2000);
            } while ((result.Status != JobStatus.Completed) && 
            (result.Status != JobStatus.Failed));
        }

        List<string> ParseConfig(ref DeviceMethodInvocationSpec spec)
        {
            var devices = new List<string>();
            using (var reader = new StreamReader(File.OpenRead(configFileName)))
            {
                var yamlDeserializer = new YamlDotNet.Serialization.Deserializer();
                var yamlObject = yamlDeserializer.Deserialize(reader);
                var yamlJsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(yamlObject);
                dynamic configJson = Newtonsoft.Json.JsonConvert.DeserializeObject(yamlJsonStr);
                dynamic invokeConfig = configJson["method-invocation-spec"];
                if (!(invokeConfig["device-id"] is null))
                {
                    JArray deviceids = invokeConfig["device-id"] as JArray;
                    for (int i=0;i<deviceids.Count;i++)
                    {
                        devices.Add(deviceids[i].ToString());
                    }
                }
                if (!(invokeConfig["device-type"] is null))
                {
                    spec.TestDeviceType = invokeConfig["device-type"].Value;
                }
                spec.MethodName = invokeConfig["method-name"].Value;
                string iiv = invokeConfig["invocation-interval"].Value;
                spec.MSecOfInvocationInterval = int.Parse(iiv);
                iiv = invokeConfig["sleep-msec-in-method"].Value;
                spec.MSecOfWaitTimeInDeviceMethod = int.Parse(iiv);
                iiv = invokeConfig["test-loop-count"].Value;
                spec.NumOfInvocationLoop = int.Parse(iiv);
                iiv = invokeConfig["data-size-of-payload"].Value;
                spec.SizeOfDataInPayload = int.Parse(iiv);
                iiv = invokeConfig["data-size-of-response"].Value;
                spec.SizeOfResponseData = int.Parse(iiv);
                iiv = invokeConfig["response-timeout"].Value;
                spec.ResponseTimeout = int.Parse(iiv);
                if (!(invokeConfig["module-id"] is null))
                {
                    spec.ModuleId = invokeConfig["module-id"].Value;
                }
            }
            return devices;
        }
    }

    public class FileLogWriter : LogWriter
    {
        string logFileName;
        FileStream fileStream ;
        TextWriter textWriter;
        public FileLogWriter(string fileName)
        {
            logFileName = fileName;
        }

        bool isConsoleUse = false;
        public void Open(bool isConsleShow= false)
        {
            try
            {
                fileStream = File.OpenWrite(logFileName);
                textWriter = new StreamWriter(fileStream);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Log File Open Failed - {0}",ex.Message);
            }
            isConsoleUse = isConsleShow;
        }
        public void Write(string content)
        {
            if (isConsoleUse)
                Console.WriteLine(content);

            textWriter.WriteLine(content);
        }

        public void Close()
        {
            textWriter.Flush();
            textWriter.Close();
            fileStream.Close();
        }
    }
}
