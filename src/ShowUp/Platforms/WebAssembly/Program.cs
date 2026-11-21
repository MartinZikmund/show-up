using Uno.UI.Hosting;

var host = UnoPlatformHostBuilder.Create()
    .App(() => new ShowUp.App())
    .UseWebAssembly()
    .Build();

await host.RunAsync();
