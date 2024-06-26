﻿using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;

namespace CloudWorker.Client.SDK.ARM;

#pragma warning disable CS8774 // Member must have a non-null value when exiting.

public class SecureEnvironmentVariable : IValidatable
{
    [Required]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecureValue { get; set; }

    [MemberNotNull(nameof(Name))]
    public void Validate()
    {
        IValidatable.Validate(this);
        if (string.IsNullOrWhiteSpace(Value) && string.IsNullOrWhiteSpace(SecureValue))
        {
            throw new ValidationError("Either 'Value' or 'SecureValue' should be provided.");
        }
    }
}

public class FileShareMount : IValidatable
{
    //TODO: The volume name must match the regex '[a-z0-9]([-a-z0-9]*[a-z0-9])?' (e.g. 'my-name'). Validate it.
    [Required]
    public string? Name { get; set; }

    [Required]
    public string? MountPath { get; set; }

    [Required]
    public string? FileShareName { get; set; }

    [Required]
    public string? StorageAccountName { get; set; }

    [Required]
    public string? StorageAccountKey { get; set; }

    [MemberNotNull(nameof(Name), nameof(MountPath), nameof(FileShareName), nameof(StorageAccountName), nameof(StorageAccountKey))]
    public void Validate()
    {
        IValidatable.Validate(this);
    }
}

public class NodeOptions : IValidatable
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CpuCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MemInGB { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; set; }

    public void Validate()
    {
        if (CpuCount.HasValue && CpuCount.Value < 1)
        {
            throw new ValidationError($"CpuCount is {CpuCount}, less than 1!");
        }
        if (MemInGB.HasValue && MemInGB.Value < 1)
        {
            throw new ValidationError($"MemInGB is {MemInGB}, less than 1!");
        }
    }
}


public enum ServiceBusQueueSku
{
    Basic,
    Standard,
    Premium
}

public class ServiceBusQueueOptions : IValidatable
{
    public readonly int[] ValidSkuCapacity = [1, 2, 4, 8, 16];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServiceBusQueueSku? Sku { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkuCapacity {  get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SizeInMB {  get; set; }

    //NOTE: The default value is "PT1M", meaning 60 seconds. And the ACI's queueOptions.messageLease (not typed in C# yet)
    //must be equal to the LockDuration. Since messageLease is not configurable in SDK, neither should LockDuration be.
    //[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    //public string? LockDuration {  get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestQueue {  get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseQueue { get; set; }

    public void Validate()
    {
        if (SkuCapacity != null && !ValidSkuCapacity.Any(e => e == SkuCapacity))
        {
            throw new ValidationError($"Invalid value {SkuCapacity} for SkuCapacity.");
        }
    }
}

#pragma warning restore CS8774 // Member must have a non-null value when exiting.
