using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EndfieldAicWeb.App;
using EndfieldAicWeb.App.Services;
using EndfieldAicWeb.Application;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<MasterDataService>();
builder.Services.AddScoped<IconCatalog>();
builder.Services.AddScoped<CalculationService>();

await builder.Build().RunAsync();
