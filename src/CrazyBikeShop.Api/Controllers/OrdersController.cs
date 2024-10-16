using System;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Api.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    readonly ILogger<OrdersController> logger;
    readonly TableServiceClient ordersTable;

    public OrdersController(IAzureClientFactory<TableServiceClient> tsFactory, ILogger<OrdersController> logger)
    {
        ordersTable = tsFactory.CreateClient("api");
        this.logger = logger;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder()
    {
        logger.LogInformation("Order received");
        
        await ordersTable.CreateTableIfNotExistsAsync("orders");
        var orders = ordersTable.GetTableClient("orders");
        
        var orderId = Guid.NewGuid().ToString();

        var orderResponse = await orders.AddEntityAsync(new Order());
        if (orderResponse.IsError) 
            return BadRequest();

        var armClient = new ArmClient(new DefaultAzureCredential());
        var job = armClient.GetContainerAppJobResource(new ResourceIdentifier(""));
        await job.StartAsync(WaitUntil.Started);
        
        var location = Url.Action($"/orders/staus/{orderId}");
        return Accepted(location);
    }
}