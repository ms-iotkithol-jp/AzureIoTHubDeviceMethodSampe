using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;

namespace egeorge.iot.execshellcommand
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false);
            app.Name = "ExecShellCommandDevice";
            app.HelpOption("-h|--help");

            var csOption = app.Option(
                "--connection-string",
                "Specify connection string for device of IoT Hub",
                CommandOptionType.SingleValue
            );

            app.OnExecute(()=>{
                string connectionString = "";
                if (!(csOption.Value() is null))
                {
                    connectionString = csOption.Value();
                }
                else
                {
                    var configFileName = "config.yml";
                    using (var reader = new StreamReader(File.OpenRead(configFileName)))
                    {
                        var yamlDeserializer = new YamlDotNet.Serialization.Deserializer();
                        var yamlObject = yamlDeserializer.Deserialize(reader);
                        var yamlJsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(yamlObject);
                        dynamic configJson = Newtonsoft.Json.JsonConvert.DeserializeObject(yamlJsonStr);
                        connectionString = configJson["connection-string"];
                    }
                }

                var deviceExecutor = new ShellCommandExecutor(connectionString);
                deviceExecutor.ConnectAsync().Wait();
                deviceExecutor.SetupDirectMethodCallback().Wait();
                Console.WriteLine("Wait for Direct Method Invocation...");

                Task.Delay(-1).Wait();

                return 0;
            });

            app.Execute(args);
        }
    }
}
