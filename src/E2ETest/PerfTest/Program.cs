﻿using Cloud.Soa;
using Cloud.Soa.Client;
using CommandLine;
using System.Diagnostics;

namespace PerfTest;

class Program
{
    class Options : Cloud.Soa.Client.QueueOptions
    {
        [Option(Hidden = true, Required = false)]
        public new string? QueueName { get; set; }

        [Option('l', "length", Default = (int)4, HelpText = "Message length")]
        public int MessageLength { get; set; }

        [Option('c', "count", Default = (int)10000, HelpText = "Number of messages to send and/or receive")]
        public int Count { get; set; }

        [Option('s', "senders", Default = (int)10)]
        public int SenderCount { get; set; }

        [Option('r', "receivers", Default = (int)100)]
        public int ReceiverCount {  get; set; }

        [Option('S', "request-queue", Default = (string)"requests")]
        public string? RequestQueueName { get; set; }

        [Option('R', "response-queue", Default = (string)"responses")]
        public string? ResponseQueueName { get; set; }

        [Option('b', "batch-size", Default = (int)1, HelpText = "Max number of messages to receive in one receive call")]
        public int BatchSize { get; set; }

        public override void Validate()
        {
            base.Validate();
            if (MessageLength <= 0)
            {
                throw new ArgumentException("MessageLength must be greater than 0!");
            }
            if (Count <= 0)
            {
                throw new ArgumentException("Count must be greater than 0!");
            }
            if (SenderCount > 0 && Count < SenderCount)
            {
                throw new ArgumentException("Count cannot be less than SenderCount!");
            }
            if (SenderCount > 0 && string.IsNullOrWhiteSpace(RequestQueueName))
            {
                throw new ArgumentException("RequestQueueName cannot be empty!");
            }
            if (ReceiverCount > 0 && string.IsNullOrWhiteSpace(ResponseQueueName))
            {
                throw new ArgumentException("ResponseQueueName cannot be empty!");
            }
        }
    }

    static int MessagesToReceive = 0;
    static int MessagesReceived = 0;
    static int MessagesFailedSending = 0;
    static CancellationTokenSource Stop = new CancellationTokenSource();

    static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
            .MapResult(RunAsync, _ => Task.FromResult(1)).ConfigureAwait(false);
    }

    static async Task<int> RunAsync(Options options)
    {
        options.Validate();

        if (options.SenderCount > 0)
        {
            var messagesToSend = (options.Count / options.SenderCount) * options.SenderCount;
            options.Count = messagesToSend;
            MessagesToReceive = messagesToSend;
        }
        else
        {
            MessagesToReceive = options.Count;
        }

        Console.WriteLine($"Messages to send and/or receive: {options.Count}");
        Console.WriteLine($"Message length: {options.MessageLength}");
        Console.WriteLine($"Message queue type: {options.QueueType}");
        Console.WriteLine($"Sender count: {options.SenderCount}");
        Console.WriteLine($"Send to: {options.RequestQueueName}");
        Console.WriteLine($"Receiver count: {options.ReceiverCount}");
        Console.WriteLine($"Receive batch size: {options.BatchSize}");
        Console.WriteLine($"Receive from: {options.ResponseQueueName}");

        var tasks = new List<Task>(2);
        var sw = Stopwatch.StartNew();

        if (options.SenderCount > 0)
        {
            tasks.Add(StartSending(options));
        }
        if (options.ReceiverCount > 0)
        {
            tasks.Add(StartReceiving(options));
        }
        if (tasks.Count > 0)
        {
            Console.WriteLine("Press any key to exit early...");
            _ = Task.Run(() =>
            {
                Console.ReadKey(true);
                Stop.Cancel();
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        sw.Stop();

        if (tasks.Count > 0)
        {
            double throughput;
            if (options.ReceiverCount > 0)
            {
                throughput = MessagesReceived / sw.Elapsed.TotalSeconds;
            }
            else
            {
                throughput = (options.Count - MessagesFailedSending) / sw.Elapsed.TotalSeconds;
            }

            Console.WriteLine($"Message length: {options.MessageLength}");
            Console.WriteLine($"Number of messages to send and/or receive: {options.Count}");
            Console.WriteLine($"Number of messages failed being sent: {MessagesFailedSending}");
            Console.WriteLine($"Adjusted number of messages to receive: {MessagesToReceive}");
            Console.WriteLine($"Actual number of messages received: {MessagesReceived}");
            Console.WriteLine($"Time elapsed: {sw.Elapsed}");
            Console.WriteLine($"End-to-end effective throughput: {throughput:f3} messages/second");
        }
        return 0;
    }

    static Task StartSending(Options options)
    {
        var message = new String('a', options.MessageLength);
        var batch = options.Count / options.SenderCount;
        var queueOpts = new Cloud.Soa.Client.QueueOptions(options) { QueueName = options.RequestQueueName };
        var tasks = new Task[options.SenderCount];
        for (var i = 0; i < options.SenderCount; i++)
        {
            var sender = QueueClient.Create(queueOpts);
            tasks[i] = StartSender(sender, message, batch);
        }
        return Task.WhenAll(tasks);
    }

    static Task StartSender(IMessageQueue sender, string message, int count)
    {
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await sender.SendAsync(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in sending: {ex}");
                    Interlocked.Increment(ref MessagesFailedSending);
                    Interlocked.Decrement(ref MessagesToReceive);
                    if (MessagesReceived >= MessagesToReceive)
                    {
                        Stop.Cancel();
                    }
                }
            });
        }
        return Task.WhenAll(tasks);
    }

    static Task StartReceiving(Options options)
    {
        var queueOpts = new Cloud.Soa.Client.QueueOptions(options) { QueueName = options.ResponseQueueName };
        var tasks = new Task[options.ReceiverCount];
        for (var i = 0; i < options.ReceiverCount; i++)
        {
            var receiver = QueueClient.Create(queueOpts);
            tasks[i] = StartReceiver(receiver, options.BatchSize);
        }
        return Task.WhenAll(tasks);
    }

    static async Task StartReceiver(IMessageQueue receiver, int batchSize)
    {
        while (!Stop.IsCancellationRequested)
        {
            IReadOnlyList<IMessage>? messages = null;
            try
            {
                messages = await receiver.WaitBatchAsync(batchSize, Stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            var tasks = new Task[messages.Count];
            for (var i = 0; i <  messages.Count; i++)
            {
                var message = messages[i];
                tasks[i] = Task.Run(async() =>
                {
                    try
                    {
                        await message.DeleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in deleting a message: {ex}");
                    }
                    Interlocked.Increment(ref MessagesReceived);
                    //NOTE: When the initial request and/or response queues are not empty and batchSize is greater than one,
                    //then more messages than MessagesToReceive may be received.
                    if (MessagesReceived >= MessagesToReceive)
                    {
                        Stop.Cancel();
                    }
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
