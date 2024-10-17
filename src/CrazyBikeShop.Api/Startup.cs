using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
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
            builder.AddTableServiceClient(Configuration["Storage:Tables"])
                .WithName("tables");

            builder.AddArmClient(Configuration["Arm:Subscription"])
                .WithName("jobs");
            
            builder.UseCredential(new DefaultAzureCredential());
            builder.ConfigureDefaults(Configuration.GetSection("AzureDefaults"));
        });

        services.AddAutoMapper(typeof(ContainerAppJobResource));
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