using Azure.Identity;
using CrazyBikeShop.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrazyBikeShop.Api;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }
    
    IConfiguration Configuration { get; }
    
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddSwagger(Configuration);
        services.AddAzureClients(builder =>
        {
            builder.AddServiceBusAdministrationClientWithNamespace(Configuration["AsbNamespace"])
                .WithName("apiAdmin");
            builder.AddServiceBusClientWithNamespace(Configuration["AsbNamespace"])
                .WithName("api");
            builder.AddTableServiceClient(Configuration["StorageConnectionString"])
                .WithName("api");
            
            builder.UseCredential(new DefaultAzureCredential());
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseSwagger(Configuration);

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }
}