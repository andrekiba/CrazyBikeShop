﻿using Azure;
using Azure.Data.Tables;

namespace CrazyBikeShop.Shared;

public class Order : ITableEntity
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
}