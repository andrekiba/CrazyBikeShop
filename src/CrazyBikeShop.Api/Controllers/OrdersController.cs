using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using CrazyBikeShop.Api.Infrastructure;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Api.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    const string OrdersTable = "orders";
    
    readonly ILogger<OrdersController> logger;
    readonly IConfiguration configuration;
    readonly TableServiceClient tables;
    readonly TableClient orders;
    readonly ArmClient armClient;

    public OrdersController(IAzureClientFactory<TableServiceClient> tsFactory, IAzureClientFactory<ArmClient> armFactory, 
        IConfiguration configuration, ILogger<OrdersController> logger)
    {
        tables = tsFactory.CreateClient("tables");
        orders = tables.GetTableClient(OrdersTable);
        armClient = armFactory.CreateClient("jobs");
        this.configuration = configuration;
        this.logger = logger;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrder newOrder)
    {
        logger.LogInformation("Order received");
        
        await tables.CreateTableIfNotExistsAsync(OrdersTable);

        var orderResponse = await orders.UpsertEntityAsync(new Order
        {
            PartitionKey = OrdersTable,
            RowKey = newOrder.Id, 
            Status = OrderStatus.Pending
        });
        
        if (orderResponse.IsError) 
            return BadRequest();

        var subId = SubscriptionResource.CreateResourceIdentifier(configuration["Arm:Subscription"]);
        var sub = armClient.GetSubscriptionResource(subId);

        const string acaJobName = "crazybikeshop-main-job-op";
        var acaJobId = ContainerAppJobResource.CreateResourceIdentifier(configuration["Arm:Subscription"], configuration["Arm:ResourceGroup"], acaJobName);
        var acaJob = armClient.GetContainerAppJobResource(acaJobId);
        acaJob = await acaJob.GetAsync();

        var template = acaJob.Data.Template;
        var env = template.Containers[0].Env;
        env.Add(new ContainerAppEnvironmentVariable{ Name = "ORDER_ID", Value = newOrder.Id });
        
        var executionTemplate = template.ToExecutionTemplate();
        
        var armOperation = await acaJob.StartAsync(WaitUntil.Started, executionTemplate);
        
        var location = Url.Action($"/orders/staus/{newOrder.Id}");
        return Accepted(location);
    }
    
    [HttpGet("status/{orderId}")]
    public async Task<IActionResult> GetOrderStatus(string orderId)
    {
        var orderResponse = await orders.GetEntityAsync<Order>(partitionKey: OrdersTable, rowKey: orderId);
        if (!orderResponse.HasValue)
            return NotFound();
        
        var order = orderResponse.Value;
        if (order.Status == OrderStatus.Completed) 
            return Redirect($"/orders/{orderId}");
        
        Response.Headers.Append("Retry-After", "10");
        return Ok(order);
    }
    
    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrder(string orderId)
    {
        var orderResponse = await orders.GetEntityAsync<Order>(partitionKey: OrdersTable, rowKey: orderId);
        if (!orderResponse.HasValue)
            return NotFound();
        
        var order = orderResponse.Value;
        return order.Status switch
        {
            OrderStatus.Pending => NotFound(),
            OrderStatus.Completed => Ok(order),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}