using FileMakerService;
using NLog.Web;

IHost host = Host.CreateDefaultBuilder(args)
               .UseNLog()
               .UseSystemd()
               .UseWindowsService(options => { options.ServiceName = "FileMakerService"; })
               .ConfigureServices(services =>
               {
                   services.AddHostedService<FileMakerWorker>();
               })
               .Build();

host.Run();