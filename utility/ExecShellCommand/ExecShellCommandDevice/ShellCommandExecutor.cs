using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

namespace egeorge.iot.execshellcommand
{
    public class ShellCommandExecutor
    {
        DeviceClient deviceClient;
        HttpClient httpClient;

        public ShellCommandExecutor(string connectionString)
        {
            deviceClient = DeviceClient.CreateFromConnectionString(connectionString);
        }

        public async Task ConnectAsync()
        {
            await deviceClient.OpenAsync();
            Console.WriteLine("IoT Hub Connected...");
        }

        public async Task DisconnectAsync()
        {
            await deviceClient.CloseAsync();
            Console.WriteLine("IoT Hub Disconnected.");
        }

        public async Task SetupDirectMethodCallback()
        {
            await deviceClient.SetMethodDefaultHandlerAsync(DirectMethodCallback, this);
            Console.WriteLine("Setup Method Invocate Request...");
        }

        private async Task<MethodResponse> DirectMethodCallback(MethodRequest methodRequest, object userContext)
        {
            MethodResponse response = null;
            Console.WriteLine("Receive Request - {0}", methodRequest.Name);
            switch (methodRequest.Name)
            {
                case "ExecShellCommand":
                    response = await ExecuteShellCommand(methodRequest);
                    break;
                case "UploadFile":
                    response = await UploadFIleCommand(methodRequest);
                    break;
                default:
                    response = new MethodResponse(400);
                    break;
            }
            return response;
        }

        private async Task<MethodResponse> UploadFIleCommand(MethodRequest methodRequest)
        {
            MethodResponse methodResponse;
            dynamic requestJson = Newtonsoft.Json.JsonConvert.DeserializeObject(methodRequest.DataAsJson);
            if (requestJson["file-name"] is null || requestJson["blob-name"] is null)
            {
                var response = new
                {
                    status = "payload should have file-name and blob-name"
                };
                methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(response)),500);
            }
            else
            {
                string fileName = requestJson["file-name"].Value;
                string blobName = requestJson["blob-name"].Value;
                if (File.Exists(fileName))
                {
                    try{
                        using (var fs = File.OpenRead(fileName))
                        {
                            await deviceClient.UploadToBlobAsync(blobName, fs);
                        }
                        methodResponse = new MethodResponse(200);
                    }
                    catch (Exception ex)
                    {
                        var response = new 
                        {
                            status = "exception",
                            message = ex.Message
                        };
                        methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(response)),500);
                    }
                }
                else
                {
                    var response = new
                    {
                        status = "file not found"
                    };
                    methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(response)),500);
                }
            }
            return methodResponse;
        }

        private async Task<MethodResponse> ExecuteShellCommand(MethodRequest methodRequest)
        {
            MethodResponse methodResponse;
            dynamic requestJson = Newtonsoft.Json.JsonConvert.DeserializeObject(methodRequest.DataAsJson);
            if (requestJson["command"] is null)
            {
                var commandExecResult = new
                {
                    status = "payload should have command!"
                };

                var commandExecResultJson = Newtonsoft.Json.JsonConvert.SerializeObject(commandExecResult);
                methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(commandExecResultJson),400);

            }
            else
            {
                string commandName = requestJson["command"].Value;
                string commandArgs = null;
                if (!(requestJson["args"] is null))
                {
                    commandArgs = requestJson["args"].Value;
                }
                var processStartInfo = new ProcessStartInfo(commandName);
                if (!string.IsNullOrEmpty(commandArgs))
                {
                    processStartInfo.Arguments = commandArgs;
                    Console.WriteLine("command:{0},args:{1}", commandName, commandArgs);
                }
                else 
                {
                    Console.WriteLine("command:{0}", commandName);
                }
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;
                processStartInfo.CreateNoWindow = true;
                string nowindow = null;
                if (!(requestJson["no-window"] is null))
                {
                    nowindow = requestJson["no-window"].Value;
                }
                if (!string.IsNullOrEmpty(nowindow))
                {
                    processStartInfo.CreateNoWindow = Boolean.Parse(nowindow);
                }
            
                string commandUrl = null;
                if (!(requestJson["command-url"] is null))
                {
                    commandUrl = requestJson["command-url"].Value;
                }
                if (!string.IsNullOrEmpty(commandUrl))
                {
                    if (httpClient == null)
                    {
                        httpClient = new HttpClient();
                    }
                    var bytes = await httpClient.GetByteArrayAsync(commandUrl);
                    using (var fs = File.OpenWrite(commandName))
                    {
                        await fs.WriteAsync(bytes, 0, bytes.Length);
                    }
                }
                Console.WriteLine("Parse comannd specification completed.");
                string output = "";
                string error = "";
                int exitCode = 0;
                try{
                    var p = new Process();
                    p.StartInfo = processStartInfo;
                    Console.WriteLine("Try execute command...");
                    p.Start();
                    Console.WriteLine("Executing...");
                    p.WaitForExit();
                    output = p.StandardOutput.ReadToEnd();
                    error = p.StandardError.ReadToEnd();
                    exitCode = p.ExitCode;
                    Console.WriteLine("Execution done.");

                    var commandExecResult = new
                    {
                        exitcode = exitCode,
                        standardoutput = output,
                        standarderror = error
                    };

                    var commandExecResultJson = Newtonsoft.Json.JsonConvert.SerializeObject(commandExecResult);
                    methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(commandExecResultJson),200);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception happens - {0}", ex.Message);
                    var commandExecResult = new
                    {
                        exception = ex.Message
                    };
                    var commandExecResultJson = Newtonsoft.Json.JsonConvert.SerializeObject(commandExecResult);
                    methodResponse = new MethodResponse(System.Text.Encoding.UTF8.GetBytes(commandExecResultJson), 500);
                }
            }
            Console.WriteLine("Completed ExecShellCommand invocation.");

            return methodResponse;
        }
    }
}