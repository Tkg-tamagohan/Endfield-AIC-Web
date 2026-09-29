using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EndfieldAicWeb.Admin;
using EndfieldAicWeb.Admin.Services;
using EndfieldAicWeb.Application;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<AdminDocumentService>();
builder.Services.AddScoped<CalculationService>();

await builder.Build().RunAsync();
