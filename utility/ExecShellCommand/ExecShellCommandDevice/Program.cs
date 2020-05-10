using System;
using System.Diagnostics;
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
                var deviceExecutor = new ShellCommandExecutor(csOption.Value());
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
