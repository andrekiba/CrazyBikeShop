using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.OrderProcessor;

public class Worker : BackgroundService
{
    const string OrdersTable = "orders";
    
    readonly IConfiguration configuration;
    readonly IHostApplicationLifetime appLifetime;
    readonly TableServiceClient tables;
    TableClient orders;
    readonly ILogger<Worker> logger;

    public Worker(IConfiguration configuration, IHostApplicationLifetime appLifetime,
        IAzureClientFactory<TableServiceClient> tsFactory, ILogger<Worker> logger)
    {
        this.configuration = configuration;
        this.appLifetime = appLifetime;
        tables = tsFactory.CreateClient("tables");
        this.logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await tables.CreateTableIfNotExistsAsync(OrdersTable, cancellationToken);
        orders = tables.GetTableClient(OrdersTable);
        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
    {
        try
        {
            var orderId = Environment.GetEnvironmentVariable("ORDER_ID");
            var orderResponse = await orders.GetEntityAsync<Shared.Order>(OrdersTable, orderId, cancellationToken: stoppingToken);

            if (!orderResponse.HasValue)
            {
                logger.LogError("Order not found");
                return;
            }
            
            var order = orderResponse.Value;
            
            var processingTime = TimeSpan.FromSeconds(new Random().Next(1,10) * 10);
            await Task.Delay(processingTime, stoppingToken);
            
            order.Status = Shared.OrderStatus.Completed;
            order.CompletedAt = DateTimeOffset.Now;
            await orders.UpdateEntityAsync(order, order.ETag, cancellationToken: stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error processing job");
        }
        finally
        {
            appLifetime.StopApplication();
        }
    }, stoppingToken);
}